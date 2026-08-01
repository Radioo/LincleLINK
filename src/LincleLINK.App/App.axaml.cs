using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LincleLINK.App.Composition;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using LincleLINK.Core.Abstractions.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace LincleLINK.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = new MainWindow();

                _services = await AppBootstrapper.BuildAsync(() => desktop.MainWindow);

                // Apply the persisted theme before the window is shown (no flash) and
                // sync the toggle to the stored value.
                var settings = _services.GetRequiredService<ISettingsStore>().Load();
                _services.GetRequiredService<IThemeManager>().Apply(settings.IsDarkTheme);

                var viewModel = _services.GetRequiredService<MainViewModel>();
                desktop.MainWindow.DataContext = viewModel;
                viewModel.IsDarkTheme = settings.IsDarkTheme;
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Startup failed: {ex}");
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
