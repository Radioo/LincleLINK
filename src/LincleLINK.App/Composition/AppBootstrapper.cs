using Microsoft.Extensions.DependencyInjection;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Composition;

namespace LincleLINK.App.Composition;

public static class AppBootstrapper
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLincleLINKCore();

        services.AddSingleton<IThemeManager, ThemeManager>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
