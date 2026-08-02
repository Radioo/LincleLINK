namespace LincleLINK.Core.Abstractions.Paths;

/// <summary>
/// Resolves the on-disk data layout. User data stays CWD-relative (v2 parity);
/// the data root is configurable via the settings <c>DataDirectory</c>.
/// </summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string DbDirectory { get; }
    string InstanceDirectory { get; }

    /// <summary>Creates <c>db/</c> and <c>instance/</c> under the data root (v2 CheckDirs).</summary>
    void EnsureCreated();
}
