namespace LincleLINK.Core.Abstractions.Dialogs;

/// <summary>
/// UI-independent dialog port. Implemented by the Avalonia app; keeps
/// mid-operation confirmations in services testable. Async because Avalonia
/// dialogs are inherently asynchronous (a sync port would deadlock the UI thread).
/// Window hosting of arbitrary view models is an App-only concern and is not part
/// of this port.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string message, string title = "");
    Task InfoAsync(string message, string title = "");
    Task ErrorAsync(string message, string title = "");

    /// <summary>Returns null when the user cancels.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Returns null when the user cancels.</summary>
    Task<string?> PickOpenFileAsync(string title, FileType fileType);
}
