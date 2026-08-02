using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using LincleLINK.App.Abstractions;
using LincleLINK.Core.Abstractions.Settings;

namespace LincleLINK.App.Services;

public sealed class ThemeManager : IThemeManager
{
    // Shared across the bootstrap and main containers so the OS-change hook is
    // installed exactly once per process.
    private static bool _titleBarHooked;

    public void Apply(AppTheme theme)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        if (OperatingSystem.IsWindows() && !_titleBarHooked)
        {
            // In System mode the OS can flip the variant while the app runs; keep
            // the native title bars in sync with the resolved variant.
            app.ActualThemeVariantChanged += (_, _) => ApplyTitleBars();
            _titleBarHooked = true;
        }

        ApplyTitleBars();
    }

    private static void ApplyTitleBars()
    {
        if (!OperatingSystem.IsWindows()
            || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var dark = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
        foreach (var window in desktop.Windows)
        {
            Win32DarkTitleBar.Apply(window, dark);
        }
    }

    /// <summary>Applies the current theme to a freshly opened window's title bar.</summary>
    public static void ApplyTitleBar(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Win32DarkTitleBar.Apply(window, dark);
    }
}
