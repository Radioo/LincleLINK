using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.App.Abstractions;
using LincleLINK.Core.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels.Base;

public abstract partial class ViewModelBase : ObservableObject, IDialogViewModel
{
    /// <summary>Window title used when this view model is hosted in a dialog window.</summary>
    public virtual string Title => "LincleLINK";

    /// <summary>Default size of the host dialog window.</summary>
    public virtual Size DialogSize => new(520, 420);

    /// <summary>Minimum size of the host dialog window.</summary>
    public virtual Size DialogMinSize => new(400, 320);

    /// <summary>Raised when the hosting dialog window should close itself.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Signals the hosting dialog window to close.</summary>
    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>User-visible activity feed shown in the dialog/panel/drawer.</summary>
    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>
    /// Single choke point for user-visible activity lines (issue #17 D5): timestamps
    /// the line for the feed and mirrors it into the diagnostic log at Debug.
    /// </summary>
    protected void AddLogLine(string line, ILogger logger)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
        logger.LogDebug("Activity: {Line}", line);
    }

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isLightTheme = true;

    [ObservableProperty]
    private bool _isSystemTheme;

    /// <summary>The currently selected theme, derived from the radio-style booleans.</summary>
    public AppTheme Theme =>
        IsDarkTheme ? AppTheme.Dark :
        IsSystemTheme ? AppTheme.System :
        AppTheme.Light;

    /// <summary>Checks the radio boolean matching <paramref name="theme"/> (no-op when already selected).</summary>
    public void SetTheme(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Dark:
                IsDarkTheme = true;
                break;
            case AppTheme.System:
                IsSystemTheme = true;
                break;
            default:
                IsLightTheme = true;
                break;
        }
    }

    /// <summary>Hook invoked when a theme is selected, after mutual-exclusion bookkeeping.</summary>
    protected virtual void OnThemeChanged(AppTheme theme)
    {
    }

    partial void OnIsDarkThemeChanged(bool value) => HandleThemeSelected(value, AppTheme.Dark);

    partial void OnIsLightThemeChanged(bool value) => HandleThemeSelected(value, AppTheme.Light);

    partial void OnIsSystemThemeChanged(bool value) => HandleThemeSelected(value, AppTheme.System);

    private void HandleThemeSelected(bool selected, AppTheme theme)
    {
        // Only a selection drives the theme; the deselection of the previously
        // checked radio re-enters here with false and must not re-apply anything.
        if (!selected)
        {
            return;
        }

        if (theme != AppTheme.Light)
        {
            IsLightTheme = false;
        }

        if (theme != AppTheme.Dark)
        {
            IsDarkTheme = false;
        }

        if (theme != AppTheme.System)
        {
            IsSystemTheme = false;
        }

        OnThemeChanged(theme);
    }
}
