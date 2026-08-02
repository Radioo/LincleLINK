using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// Linux shell adapter: broadcasts <c>com.canonical.Unity.LauncherEntry.Update</c>
/// signals on the session bus. KDE Plasma's task manager, GNOME dock extensions
/// (Dash to Dock etc.), Cairo-Dock and friends all consume this protocol; the
/// entry is matched to the app by its installed <c>.desktop</c> file id. The
/// <c>urgent</c> property maps to the desktop's demands-attention highlight,
/// which the shell clears itself when the window is activated.
/// </summary>
[SupportedOSPlatform("linux")]
[ExcludeFromCodeCoverage]
internal sealed class UnityLauncherTaskbarBackend : ITaskbarProgressBackend
{
    // Must match the installed desktop file id: inside Flatpak the desktop file
    // is exported as <app-id>.desktop (FLATPAK_ID is set by the sandbox), bare
    // installs use packaging/linux/LincleLINK.desktop.
    private static readonly string AppUri =
        Environment.GetEnvironmentVariable("FLATPAK_ID") is { Length: > 0 } flatpakId
            ? $"application://{flatpakId}.desktop"
            : "application://LincleLINK.desktop";
    private const string ObjectPath = "/com/canonical/unity/launcherentry/1";
    private const string InterfaceName = "com.canonical.Unity.LauncherEntry";

    private DBusConnection? _connection;
    private bool _connectionFailed;
    private bool _urgent;

    public void SetValue(double percent)
    {
        _urgent = false;
        Send(Math.Clamp(percent, 0, 100) / 100d, progressVisible: true);
    }

    public void SetIndeterminate()
    {
        // The LauncherEntry protocol has no indeterminate mode; an empty bar at
        // 0% is the closest honest representation until a value arrives.
        _urgent = false;
        Send(0d, progressVisible: true);
    }

    public void Clear()
    {
        _urgent = false;
        Send(0d, progressVisible: false);
    }

    public void RequestAttention()
    {
        _urgent = true;
        Send(0d, progressVisible: false);
    }

    private void Send(double progress, bool progressVisible)
    {
        var connection = GetConnection();
        if (connection is null)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "Update",
            signature: "sa{sv}");
        writer.WriteString(AppUri);
        var dict = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("progress");
        writer.WriteVariantDouble(progress);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("progress-visible");
        writer.WriteVariantBool(progressVisible);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("urgent");
        writer.WriteVariantBool(_urgent);
        writer.WriteDictionaryEnd(dict);

        // TrySendMessage is a no-op (false) until the async connect completes;
        // early progress reports are simply dropped, later ones get through.
        connection.TrySendMessage(writer.CreateMessage());
    }

    private DBusConnection? GetConnection()
    {
        if (_connectionFailed)
        {
            return null;
        }

        if (_connection is null)
        {
            var address = DBusAddress.Session;
            if (address is null)
            {
                // No session bus (e.g. bare console session): disable quietly.
                _connectionFailed = true;
                return null;
            }

            _connection = new DBusConnection(address);
            _ = ConnectAsync(_connection);
        }

        return _connection;
    }

    private async Task ConnectAsync(DBusConnection connection)
    {
        try
        {
            await connection.ConnectAsync();
        }
        catch
        {
            _connectionFailed = true;
            connection.Dispose();
            _connection = null;
        }
    }
}
