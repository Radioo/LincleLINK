using Avalonia;
using LincleLINK.App.Composition;
using LincleLINK.App.Services;
using LincleLINK.Core.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace LincleLINK.App;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Seed the file-logging switch from persisted settings before the pipeline
        // activates, so an enabled toggle captures bootstrap and startup events too.
        var settings = new JsonSettingsStore(
            AppConfig.SettingsFile, NullLogger<JsonSettingsStore>.Instance).Load();
        FileLoggingSwitch.Enabled = settings.SaveLogToFile;

        SerilogPipeline.Initialize(AppConfig.LogDirectory);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AvaloniaLogSink.Install();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();
    }
}
