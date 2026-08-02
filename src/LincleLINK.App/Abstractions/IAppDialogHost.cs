namespace LincleLINK.App.Abstractions;

/// <summary>
/// App-side complement to <see cref="Core.Abstractions.Dialogs.IDialogService"/>:
/// hosts a view model's view (via the ViewLocator) in a modal window. Lives in
/// the abstractions namespace so view models can depend on it without pulling in
/// the concrete Services layer.
/// </summary>
public interface IAppDialogHost
{
    Task ShowDialogAsync(IDialogViewModel vm);
}
