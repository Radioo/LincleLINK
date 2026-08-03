using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Controls;
using LincleLINK.Core.Abstractions.Dialogs;

namespace LincleLINK.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>: styled message boxes,
/// StorageProvider file/folder pickers, and VM-window hosting via the ViewLocator.
/// </summary>
public sealed class DialogService : IDialogService, IAppDialogHost
{
    // Taskbar/title-bar icon for code-created dialog windows (used on Windows/Linux;
    // macOS takes the icon from the .app bundle instead).
    private static readonly WindowIcon AppIcon =
        new(AssetLoader.Open(new Uri("avares://LincleLINK/Assets/LL_logo.ico")));

    private readonly Func<Window?> _ownerProvider;

    public DialogService(Func<Window?> ownerProvider)
    {
        _ownerProvider = ownerProvider;
    }

    public async Task<bool> ConfirmAsync(string message, string title = "")
        => await ShowMessageAsync(message, title, MessageDialogButtons.YesNo) == MessageDialogResult.Yes;

    public Task InfoAsync(string message, string title = "")
        => ShowMessageAsync(message, title, MessageDialogButtons.Ok);

    public Task ErrorAsync(string message, string title = "")
        => ShowMessageAsync(message, title, MessageDialogButtons.Ok);

    public async Task<ConflictChoice> AskConflictAsync(string message, string title = "")
        => await ShowMessageAsync(message, title, MessageDialogButtons.ReplaceSkipCancel) switch
        {
            MessageDialogResult.Replace => ConflictChoice.Replace,
            MessageDialogResult.Skip => ConflictChoice.Skip,
            _ => ConflictChoice.Cancel,
        };

    public async Task<string?> PickFolderAsync(string title, string? startDirectory = null)
    {
        var storage = GetStorageProvider();

        // TryGetFolderFromPathAsync returns null for a missing/inaccessible path,
        // which falls back to the picker's platform default location.
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(startDirectory))
        {
            startLocation = await storage.TryGetFolderFromPathAsync(startDirectory);
        }

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, FileType fileType)
    {
        var storage = GetStorageProvider();
        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileType.Label) { Patterns = [.. fileType.Patterns] }],
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Hosts a view model's view (resolved via the app ViewLocator) in a modal
    /// window. The window is user-resizable with a fixed default size, so content
    /// that grows (e.g. a log panel) scrolls instead of stretching the window.
    /// Completes when the view closes its host window.
    /// </summary>
    public async Task ShowDialogAsync(IDialogViewModel vm)
    {
        var window = new Window
        {
            Title = vm.Title,
            Content = vm,
            Width = vm.DialogSize.Width,
            Height = vm.DialogSize.Height,
            MinWidth = vm.DialogMinSize.Width,
            MinHeight = vm.DialogMinSize.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Icon = AppIcon,
        };
        ThemeManager.ApplyTitleBar(window);

        // Close the host window through a direct reference when the view model asks
        // (robust; the view's VisualRoot is not reliably the hosting window here).
        vm.CloseRequested += (_, _) => window.Close();

        // Settle on the Closed event in both branches: closing via the title-bar X
        // must never leave the caller awaiting the dialog.
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();

        var owner = _ownerProvider();
        if (owner is not null)
        {
            var show = window.ShowDialog(owner);
            await Task.WhenAny(show, closed.Task);
        }
        else
        {
            // No owner (e.g. before the main window exists): show non-modally but
            // still wait for close so the contract "completes when the host window
            // closes" holds in both branches.
            window.Show();
            await closed.Task;
        }
    }

    private async Task<MessageDialogResult> ShowMessageAsync(string message, string title, MessageDialogButtons buttons)
    {
        var dialog = new MessageDialog();
        dialog.Configure(message, buttons);
        var window = new Window
        {
            Title = title,
            Content = dialog,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Icon = AppIcon,
        };
        ThemeManager.ApplyTitleBar(window);

        var tcs = new TaskCompletionSource<MessageDialogResult>();
        dialog.ResultChosen += result =>
        {
            tcs.TrySetResult(result);
            window.Close();
        };

        // Closing via the title-bar X must not hang the caller: treat it as the
        // safe dismissal result for the button set (Ok, No for YesNo, Cancel for
        // the conflict prompt).
        window.Closed += (_, _) => tcs.TrySetResult(buttons switch
        {
            MessageDialogButtons.Ok => MessageDialogResult.Ok,
            MessageDialogButtons.ReplaceSkipCancel => MessageDialogResult.Cancel,
            _ => MessageDialogResult.No,
        });

        var owner = _ownerProvider();
        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        return await tcs.Task;
    }

    private IStorageProvider GetStorageProvider()
    {
        var owner = _ownerProvider();
        var topLevel = owner is not null ? TopLevel.GetTopLevel(owner) : null;
        if (topLevel is not { IsVisible: true }
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            topLevel = desktop.Windows.FirstOrDefault(w => w.IsVisible) ?? topLevel;
        }

        return topLevel?.StorageProvider
            ?? throw new InvalidOperationException("No window is available to host a file picker.");
    }
}
