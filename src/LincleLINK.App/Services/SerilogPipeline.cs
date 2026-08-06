using Serilog;
using Serilog.Events;

namespace LincleLINK.App.Services;

/// <summary>
/// Builds the single app-wide Serilog pipeline (issue #17 D1): a console sink that
/// is always on, and a file sink gated per-event by <see cref="FileLoggingSwitch"/>
/// so the Settings toggle works live. Core never sees Serilog - it logs to ILogger.
/// </summary>
public static class SerilogPipeline
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Activates the pipeline as the global <see cref="Log"/> logger. Called once at
    /// the top of <c>Program.Main</c> before Avalonia starts so bootstrap and
    /// composition-root failures are captured from the first line.
    /// </summary>
    public static void Initialize(string logDirectory)
    {
        Log.Logger = BuildConfiguration(logDirectory).CreateLogger();

        if (FileLoggingSwitch.Enabled)
        {
            Directory.CreateDirectory(logDirectory);
            WriteHeader();
        }
    }

    public static LoggerConfiguration BuildConfiguration(string logDirectory)
        => new LoggerConfiguration()
            .MinimumLevel.ControlledBy(FileLoggingSwitch.LevelSwitch)
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Information,
                standardErrorFromLevel: LogEventLevel.Error,
                outputTemplate: OutputTemplate)
            .WriteTo.Conditional(
                _ => FileLoggingSwitch.Enabled,
                writeTo => writeTo.File(
                    Path.Combine(logDirectory, "linclelink-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: OutputTemplate));

    /// <summary>
    /// One self-identifying event written on every enable (startup or toggle flip),
    /// so any log file pasted into a bug report names its own app, OS and runtime.
    /// </summary>
    public static void WriteHeader()
    {
        var version = typeof(SerilogPipeline).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Log.Information(
            "LincleLINK {Version} starting on {Os} ({Architecture}, {Runtime})",
            version,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    }
}
