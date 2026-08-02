using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using LincleLINK.App.Abstractions;

namespace LincleLINK.App.Services;

public sealed class ThemeManager : IThemeManager
{
    public void Apply(bool dark)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        if (OperatingSystem.IsWindows()
            && Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                Win32DarkTitleBar.Apply(window, dark);
            }
        }
    }

    /// <summary>Applies the current theme to a freshly opened window's title bar.</summary>
    public static void ApplyTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        Win32DarkTitleBar.Apply(window, dark);
    }
}
