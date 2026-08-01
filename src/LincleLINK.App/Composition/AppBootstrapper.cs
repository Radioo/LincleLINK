using Avalonia.Controls;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using LincleLINK.Core.Composition;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace LincleLINK.App.Composition;

public static class AppBootstrapper
{
    public static async Task<ServiceProvider> BuildAsync(Func<Window?> ownerProvider)
    {
        var settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LincleLINK",
            "settings.json");

        string dataDirectory;
        using (var bootstrap = CreateBootstrapContainer(settingsFile, ownerProvider))
        {
            // Apply the persisted theme before any window is shown (no flash) so the
            // first-run dialog and the main window both render with the stored theme.
            var settingsStore = bootstrap.GetRequiredService<ISettingsStore>();
            var settings = settingsStore.Load();
            var themeManager = bootstrap.GetRequiredService<IThemeManager>();
            themeManager.Apply(settings.IsDarkTheme);

            var firstLaunch = bootstrap.GetRequiredService<FirstLaunchService>();
            var result = firstLaunch.Resolve();

            if (result.Action == FirstLaunchAction.PromptForDirectory)
            {
                // The first-run dialog is hosted modally, which requires a visible owner.
                ownerProvider()?.Show();

                var host = bootstrap.GetRequiredService<IAppDialogHost>();
                var dialogs = bootstrap.GetRequiredService<IDialogService>();
                var vm = new FirstRunViewModel(
                    dialogs, themeManager, result.DataDirectory, result.HasLegacyV2Data,
                    result.LegacyDarkTheme ?? settings.IsDarkTheme);
                dataDirectory = await ShowFirstRunAsync(vm, host);

                // Persist the explicit choice (directory + theme) made in the dialog.
                settingsStore.Save(new AppSettings(vm.IsDarkTheme, dataDirectory, Environment.ProcessorCount));
            }
            else
            {
                dataDirectory = result.DataDirectory;

                if (!settingsStore.Exists)
                {
                    firstLaunch.CompleteFirstLaunch(dataDirectory);
                }
            }
        }

        var paths = new AppPaths(dataDirectory);
        paths.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLincleLINKCore();
        services.AddSingleton<ISettingsStore>(new JsonSettingsStore(settingsFile));
        services.AddSingleton<IAppPaths>(paths);

        var dialogService = new DialogService(ownerProvider);
        services.AddSingleton(dialogService);
        services.AddSingleton<IDialogService>(dialogService);
        services.AddSingleton<IAppDialogHost>(dialogService);

        services.AddSingleton<IThemeManager, ThemeManager>();
        // Transient so the Add Instance dialog starts fresh (no remembered fields/log).
        services.AddTransient<AddInstanceViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Func<AddInstanceViewModel>>(sp => () => sp.GetRequiredService<AddInstanceViewModel>());

        return services.BuildServiceProvider();
    }

    private static async Task<string> ShowFirstRunAsync(FirstRunViewModel vm, IAppDialogHost host)
    {
        var chosen = new TaskCompletionSource<string>();
        vm.Confirmed += (_, dir) => chosen.TrySetResult(dir);

        await host.ShowDialogAsync(vm);

        // Fall back to the current candidate if the window was closed without confirming.
        return chosen.Task.IsCompleted ? await chosen.Task : vm.DataDirectory;
    }

    private static ServiceProvider CreateBootstrapContainer(string settingsFile, Func<Window?> ownerProvider)
    {
        var dialogService = new DialogService(ownerProvider);

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(new JsonSettingsStore(settingsFile));
        services.AddSingleton<IDialogService>(dialogService);
        services.AddSingleton<IAppDialogHost>(dialogService);
        services.AddSingleton<IThemeManager, ThemeManager>();
        services.AddSingleton<FirstLaunchService>();
        return services.BuildServiceProvider();
    }
}
