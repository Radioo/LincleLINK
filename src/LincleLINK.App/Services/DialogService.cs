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

    public async Task<string?> PickFolderAsync(string title)
    {
        var storage = GetStorageProvider();
        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
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

        var owner = _ownerProvider();
        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            // No owner (e.g. before the main window exists): show non-modally but
            // still wait for close so the contract "completes when the host window
            // closes" holds in both branches.
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
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
        // safe dismissal result for the button set (Ok, or No for YesNo).
        window.Closed += (_, _) => tcs.TrySetResult(
            buttons == MessageDialogButtons.Ok ? MessageDialogResult.Ok : MessageDialogResult.No);

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
