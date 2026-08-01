using LincleLINK.App.ViewModels;

namespace LincleLINK.App.Services;

/// <summary>
/// App-side complement to <see cref="Core.Abstractions.Dialogs.IDialogService"/>:
/// hosts a view model's view (via the ViewLocator) in a modal window.
/// </summary>
public interface IAppDialogHost
{
    Task ShowDialogAsync(ViewModelBase vm);
}
