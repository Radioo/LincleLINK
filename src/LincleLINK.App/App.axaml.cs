using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Composition;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LincleLINK.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        BrandTheme.Apply(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = new MainWindow();

                _services = await AppBootstrapper.BuildAsync(() => desktop.MainWindow);
                var logger = _services.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Bootstrap completed; starting the main window");

                var settings = _services.GetRequiredService<ISettingsStore>().Load();

                // Ensure the SQLite schema exists before the first query. Fresh
                // installs have no JSON to trigger the migration below, but the
                // first repository call would crash on a missing table without
                // this (plan 13 §7/§8).
                var migration = _services.GetRequiredService<StorageMigrationService>();
                await migration.EnsureSchemaAsync();
                logger.LogInformation("Database schema ensured");

                // Set the DataContext BEFORE any dialog can show the main window:
                // with a null DataContext every page-visibility binding is unresolved
                // and all shell pages render stacked on top of each other (the
                // migration-dialog glitch). The VM constructor is cheap and runs no
                // queries; the data refresh still happens in InitializeAsync later.
                var viewModel = _services.GetRequiredService<MainViewModel>();
                viewModel.SetTheme(settings.Theme);
                viewModel.ThreadCount = settings.HashThreadCount;
                // Resolved to an absolute path by FirstLaunchService before this
                // point; the fallback only covers a hand-edited settings file.
                viewModel.DataDirectory = settings.DataDirectory ?? string.Empty;
                desktop.MainWindow.DataContext = viewModel;

                // Forced one-time JSON → SQLite migration before the main window loads
                // (plan 13 §7): users with legacy instance/*.json manifests get a
                // non-dismissable progress window; new installs skip straight through.
                if (migration.NeedsMigration())
                {
                    logger.LogInformation("Legacy JSON manifests found; running the storage migration");
                    if (!desktop.MainWindow.IsVisible)
                    {
                        desktop.MainWindow.Show();
                    }

                    var host = _services.GetRequiredService<IAppDialogHost>();
                    var migrationVm = _services.GetRequiredService<StorageMigrationViewModel>();
                    var run = migrationVm.RunAsync();
                    await host.ShowDialogAsync(migrationVm);
                    await run;
                }

                // The first-run/migration dialogs show the main window before Opened
                // can drive the initial refresh with a DataContext in place; fire it
                // directly here when the window is already visible.
                if (desktop.MainWindow.IsVisible)
                {
                    logger.LogInformation("Running the initial library refresh");
                    await viewModel.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                // Log, then surface the failure and exit cleanly with a non-zero
                // code. Rethrowing from an async-void override would surface as an
                // unhandled crash; a Shutdown keeps the process exit observable
                // (CI, scripts). A corrupt or locked linclelink.db (EnsureSchemaAsync)
                // must not die silently, so show an error dialog when the services
                // needed to render one exist. The console sink covers the report when
                // services are not available yet.
                if (_services is { } services)
                {
                    services.GetRequiredService<ILogger<App>>().LogCritical(ex, "Startup failed");
                }
                else
                {
                    Log.Logger.Fatal(ex, "Startup failed");
                }

                await ReportStartupFailureAsync(desktop, ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ReportStartupFailureAsync(
        IClassicDesktopStyleApplicationLifetime desktop, Exception ex)
    {
        if (_services is { } services)
        {
            try
            {
                if (desktop.MainWindow is { IsVisible: false } mainWindow)
                {
                    mainWindow.Show();
                }

                var dialogs = services.GetRequiredService<IDialogService>();
                await dialogs.ErrorAsync($"LincleLINK could not start:\n\n{ex.Message}", "Startup failed");
            }
            catch
            {
                // No UI to report through (dialog infrastructure not ready yet);
                // the console line above remains the record.
            }
        }

        desktop.Shutdown(1);
    }
}
