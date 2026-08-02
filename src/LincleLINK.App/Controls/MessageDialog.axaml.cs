using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LincleLINK.App.Controls;

public enum MessageDialogButtons
{
    Ok,
    YesNo,
}

public enum MessageDialogResult
{
    Ok,
    Yes,
    No,
}

/// <summary>
/// A styled message box used by <see cref="Services.DialogService"/> for
/// Confirm/Info/Error. Properties are set from code, not bound.
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

        if (buttons == MessageDialogButtons.Ok)
        {
            OkButton.IsVisible = true;
            OkButton.Focus();
        }
        else
        {
            YesButton.IsVisible = true;
            NoButton.IsVisible = true;
            YesButton.Focus();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Ok);

    private void OnYes(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.Yes);

    private void OnNo(object? sender, RoutedEventArgs e) => ResultChosen?.Invoke(MessageDialogResult.No);
}
