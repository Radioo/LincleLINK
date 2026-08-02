using Avalonia;

namespace LincleLINK.App.Abstractions;

/// <summary>
/// Minimal contract a view model must expose for a dialog host window to be
/// built around it. Kept independent of the concrete view model base class so
/// the dialog-hosting port never needs to reference the ViewModels namespace.
/// </summary>
public interface IDialogViewModel
{
    /// <summary>Window title shown when this view model is hosted in a dialog window.</summary>
    string Title { get; }

    /// <summary>Default size of the host dialog window.</summary>
    Size DialogSize { get; }

    /// <summary>Minimum size of the host dialog window.</summary>
    Size DialogMinSize { get; }

    /// <summary>Raised when the hosting dialog window should close itself.</summary>
    event EventHandler? CloseRequested;
}
