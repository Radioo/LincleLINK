using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.ViewModels;

public partial class AddInstanceViewModel : ViewModelBase
{
    private readonly InstanceService _service;
    private readonly IDialogService _dialogs;

    /// <summary>Raised with true on success (the host window closes); false on explicit close.</summary>
    public event EventHandler<bool>? CloseRequested;

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _dataPath = string.Empty;

    [ObservableProperty]
    private bool _isCopyChecked = true;

    [ObservableProperty]
    private bool _isMoveChecked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand), nameof(MakeInstanceCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    public AddInstanceViewModel(InstanceService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
    }

    public CopyMoveMode Mode => IsCopyChecked ? CopyMoveMode.Copy : CopyMoveMode.Move;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.PickFolderAsync("Select data path");
        if (path is not null)
        {
            DataPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task MakeInstanceAsync()
    {
        IsBusy = true;
        var log = new Progress<string>(LogLines.Add);
        var percent = new Progress<double>(p => Progress = p);

        try
        {
            var result = await _service.CreateInstanceAsync(new AddInstanceRequest(InstanceName, DataPath, Mode), log, percent);

            if (result.Success)
            {
                CloseRequested?.Invoke(this, true);
            }
            else if (result.Error is not null)
            {
                await _dialogs.ErrorAsync(result.Error, "Add instance");
            }
            else
            {
                LogLines.Add("Operation cancelled.");
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ErrorAsync(ex.Message, "Add instance");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInteract() => !IsBusy;
}
