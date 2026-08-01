using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
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
    public static ServiceProvider Build()
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
            dataDirectory = result.DataDirectory;

            if (result.Action == FirstLaunchAction.PromptForDirectory)
            {
                // M2: show the FirstRunWindow picker. Until then, default to CWD.
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
        services.AddSingleton<IThemeManager, ThemeManager>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateBootstrapContainer(string settingsFile)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(new JsonSettingsStore(settingsFile));
        services.AddSingleton<FirstLaunchService>();
        return services.BuildServiceProvider();
    }
}
