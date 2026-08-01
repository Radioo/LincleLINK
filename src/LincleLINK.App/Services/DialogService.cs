using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LincleLINK.App.Controls;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;

namespace LincleLINK.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>: styled message boxes,
/// StorageProvider file/folder pickers, and VM-window hosting via the ViewLocator.
/// </summary>
public sealed class DialogService : IDialogService, IAppDialogHost
{
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

    public async Task<string?> PickOpenFileAsync(string title, string filter)
    {
        var storage = GetStorageProvider();
        var (label, patterns) = ParseFilter(filter);
        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(label) { Patterns = patterns }],
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Hosts a view model's view (resolved via the app ViewLocator) in a modal
    /// window. Completes when the view closes its host window.
    /// </summary>
    public async Task ShowDialogAsync(ViewModelBase vm)
    {
        var window = new Window
        {
            Content = vm,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        ThemeManager.ApplyTitleBar(window);

        var owner = _ownerProvider();
        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async Task<MessageDialogResult> ShowMessageAsync(string message, string title, MessageDialogButtons buttons)
    {
        var dialog = new MessageDialog { Message = message, Buttons = buttons };
        var window = new Window
        {
            Title = title,
            Content = dialog,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        ThemeManager.ApplyTitleBar(window);

        var tcs = new TaskCompletionSource<MessageDialogResult>();
        dialog.ResultChosen += result =>
        {
            tcs.TrySetResult(result);
            window.Close();
        };

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
        var topLevel = TopLevel.GetTopLevel(owner);
        return topLevel?.StorageProvider
            ?? throw new InvalidOperationException("No window is available to host a file picker.");
    }

    private static (string Label, string[] Patterns) ParseFilter(string filter)
    {
        var parts = filter.Split('|');
        var label = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "Files";
        var patterns = parts.Length > 1
            ? parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["*.*"];

        return (label, patterns);
    }
}
