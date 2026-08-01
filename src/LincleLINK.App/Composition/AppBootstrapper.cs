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
        using (var bootstrap = CreateBootstrapContainer(settingsFile))
        {
            var firstLaunch = bootstrap.GetRequiredService<FirstLaunchService>();
            var result = firstLaunch.Resolve();

            if (result.Action == FirstLaunchAction.PromptForDirectory)
            {
                var host = bootstrap.GetRequiredService<IAppDialogHost>();
                var dialogs = bootstrap.GetRequiredService<IDialogService>();
                var vm = new FirstRunViewModel(dialogs, result.DataDirectory, result.HasLegacyV2Data);
                dataDirectory = await ShowFirstRunAsync(vm, host);
            }
            else
            {
                dataDirectory = result.DataDirectory;
            }

            if (!bootstrap.GetRequiredService<ISettingsStore>().Exists)
            {
                firstLaunch.CompleteFirstLaunch(dataDirectory);
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
        services.AddSingleton<FirstRunViewModel>();
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

    private static ServiceProvider CreateBootstrapContainer(string settingsFile)
    {
        var dialogService = new DialogService(() => null);

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(new JsonSettingsStore(settingsFile));
        services.AddSingleton<IDialogService>(dialogService);
        services.AddSingleton<IAppDialogHost>(dialogService);
        services.AddSingleton<FirstLaunchService>();
        return services.BuildServiceProvider();
    }
}
