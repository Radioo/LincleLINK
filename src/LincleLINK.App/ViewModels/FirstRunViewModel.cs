using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels.Base;
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
    private readonly IThemeManager _themeManager;

    public event EventHandler<string>? Confirmed;

    public override string Title => "First launch";

    // Wide, short dialog so the prompt reads horizontally rather than vertically.
    public override Size DialogSize => new(720, 300);
    public override Size DialogMinSize => new(640, 260);

    [ObservableProperty]
    private string _dataDirectory;

    [ObservableProperty]
    private string _status = string.Empty;

    public FirstRunViewModel(
        IDialogService dialogs,
        IThemeManager themeManager,
        string defaultDirectory,
        bool hasLegacyV2Data,
        bool defaultDarkTheme)
    {
        _dialogs = dialogs;
        _themeManager = themeManager;
        _dataDirectory = defaultDirectory;
        IsDarkTheme = defaultDarkTheme;
        IsLightTheme = !defaultDarkTheme;
        _status = hasLegacyV2Data
            ? "Existing v2 data detected in the current directory."
            : "Choose the folder that contains (or will contain) your db/ and instance/ data.";
    }

    protected override void OnThemeChanged(bool dark) => _themeManager.Apply(dark);

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
