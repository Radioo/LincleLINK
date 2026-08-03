using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LincleLINK.App.Controls;

public enum MessageDialogButtons
{
    Ok,
    YesNo,

    /// <summary>Three-way conflict prompt: Replace / Skip existing / Cancel (plan 14 §3).</summary>
    ReplaceSkipCancel,
}

public enum MessageDialogResult
{
    Ok,
    Yes,
    No,
    Replace,
    Skip,
    Cancel,
}

/// <summary>
/// A styled message box used by <see cref="Services.DialogService"/> for
/// Confirm/Info/Error and the three-way conflict prompt. Properties are set from
/// code, not bound.
/// </summary>
public partial class MessageDialog : UserControl
{
    public event Action<MessageDialogResult>? ResultChosen;

    public MessageDialog()
    {
        InitializeComponent();
        YesButton.IsVisible = false;
        NoButton.IsVisible = false;
    }

    /// <summary>
    /// Sets the message text and the visible button set. Called once from code
    /// (never bound), which is why this is a method rather than properties.
    /// </summary>
    public void Configure(string message, MessageDialogButtons buttons)
    {
        MessageText.Text = message;

        switch (buttons)
        {
            case MessageDialogButtons.Ok:
                OkButton.IsVisible = true;
                OkButton.Focus();
                break;
            case MessageDialogButtons.ReplaceSkipCancel:
                ReplaceButton.IsVisible = true;
                SkipButton.IsVisible = true;
                CancelButton.IsVisible = true;
                // Focus the non-destructive choice: Skip leaves existing files alone.
                SkipButton.Focus();
                break;
            default:
                YesButton.IsVisible = true;
                NoButton.IsVisible = true;
                YesButton.Focus();
                break;
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Ok);

    private void OnYes(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Yes);

    private void OnNo(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.No);

    private void OnReplace(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Replace);

    private void OnSkip(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Skip);

    private void OnCancel(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Cancel);
}
