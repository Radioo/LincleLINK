using Avalonia.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using SerilogLevel = Serilog.Events.LogEventLevel;

namespace LincleLINK.App.Services;

/// <summary>
/// Bridges Avalonia's own diagnostics into the Serilog pipeline (issue #17 D4):
/// binding errors and framework warnings land in the same log instead of a
/// debugger-only trace. Only Warning+ is forwarded; Avalonia chatter stays out.
/// </summary>
public sealed class AvaloniaLogSink : ILogSink
{
    /// <summary>Registers this sink as the app-wide Avalonia logger sink.</summary>
    public static void Install()
    {
        Logger.Sink = new AvaloniaLogSink();
    }

    public bool IsEnabled(AvaloniaLevel level, string area)
        => level >= AvaloniaLevel.Warning;

    public void Log(AvaloniaLevel level, string area, object? source, string message)
        => Write(level, area, source, message, Array.Empty<object?>());

    public void Log(AvaloniaLevel level, string area, object? source, string message, object?[] propertyValues)
        => Write(level, area, source, message, propertyValues);

    private static void Write(
        AvaloniaLevel level,
        string area,
        object? source,
        string message,
        object?[] propertyValues)
    {
        // Gate here as well as in IsEnabled: the sink must never leak Debug/
        // Information chatter even if a caller invokes Log directly (issue #17 D4).
        if (level < AvaloniaLevel.Warning)
        {
            return;
        }

        var mapped = level switch
        {
            AvaloniaLevel.Verbose => SerilogLevel.Verbose,
            AvaloniaLevel.Debug => SerilogLevel.Debug,
            AvaloniaLevel.Information => SerilogLevel.Information,
            AvaloniaLevel.Warning => SerilogLevel.Warning,
            AvaloniaLevel.Error => SerilogLevel.Error,
            _ => SerilogLevel.Fatal,
        };

        var log = Serilog.Log.Logger
            .ForContext("SourceContext", "Avalonia")
            .ForContext("AvaloniaArea", area)
            .ForContext("AvaloniaSource", source);

        if (propertyValues.Length > 0)
        {
            log.Write(mapped, message, propertyValues);
        }
        else
        {
            log.Write(mapped, message);
        }
    }
}
