using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.App.Abstractions;

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

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private bool _isLightTheme = true;

    /// <summary>Hook invoked when the theme switches to dark/light after mutual-exclusion bookkeeping.</summary>
    protected virtual void OnThemeChanged(bool dark)
    {
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (value)
        {
            IsLightTheme = false;
        }

        OnThemeChanged(value);
    }

    partial void OnIsLightThemeChanged(bool value)
    {
        if (value)
        {
            IsDarkTheme = false;
        }
    }
}
