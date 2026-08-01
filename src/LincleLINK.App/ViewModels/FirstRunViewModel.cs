using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Application;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// First-launch data-directory prompt (plan 03 §5 / 08 §3). Raised <see cref="Confirmed"/>
/// with the chosen directory; the host window closes itself.
/// </summary>
public partial class FirstRunViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    public event EventHandler<string>? Confirmed;

    public override string Title => "First launch";

    [ObservableProperty]
    private string _dataDirectory;

    [ObservableProperty]
    private string _status = string.Empty;

    public FirstRunViewModel(IDialogService dialogs, string defaultDirectory, bool hasLegacyV2Data)
    {
        _dialogs = dialogs;
        _dataDirectory = defaultDirectory;
        _status = hasLegacyV2Data
            ? "Existing v2 data detected in the current directory."
            : "Choose the folder that contains (or will contain) your db/ and instance/ data.";
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.PickFolderAsync("Select data directory");
        if (path is not null)
        {
            DataDirectory = path;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            Status = "Choose a folder before continuing.";
            return;
        }

        Confirmed?.Invoke(this, DataDirectory);
        RequestClose();
    }
}
