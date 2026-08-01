using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LincleLINK.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
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
}
