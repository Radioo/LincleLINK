namespace LincleLINK.Core.Abstractions.Dialogs;

/// <summary>
/// UI-independent dialog port. Implemented by the Avalonia app; keeps
/// mid-operation confirmations in services testable. Window hosting of arbitrary
/// view models is an App-only concern and is not part of this port.
/// </summary>
public interface IDialogService
{
    bool Confirm(string message, string title = "");
    void Info(string message, string title = "");
    void Error(string message, string title = "");

    /// <summary>Returns null when the user cancels.</summary>
    string? PickFolder(string title);

    /// <summary>Returns null when the user cancels.</summary>
    string? PickOpenFile(string title, string filter);
}
