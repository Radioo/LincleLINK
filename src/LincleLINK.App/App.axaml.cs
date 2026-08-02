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

                var settings = _services.GetRequiredService<ISettingsStore>().Load();

                // Ensure the SQLite schema exists before the first query. Fresh
                // installs have no JSON to trigger the migration below, but the
                // first repository call would crash on a missing table without
                // this (plan 13 §7/§8).
                var migration = _services.GetRequiredService<StorageMigrationService>();
                await migration.EnsureSchemaAsync();

                // Forced one-time JSON → SQLite migration before the main window loads
                // (plan 13 §7): users with legacy instance/*.json manifests get a
                // non-dismissable progress window; new installs skip straight through.
                if (migration.NeedsMigration())
                {
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

                var viewModel = _services.GetRequiredService<MainViewModel>();
                desktop.MainWindow.DataContext = viewModel;
                viewModel.SetTheme(settings.Theme);
                viewModel.ThreadCount = settings.HashThreadCount;

                // The first-run dialog shows the main window before the DataContext is
                // set, so Opened cannot drive the initial refresh in that case; fire it
                // directly here when the window is already visible.
                if (desktop.MainWindow.IsVisible)
                {
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
                // needed to render one exist.
                Console.Error.WriteLine($"Startup failed: {ex}");
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
