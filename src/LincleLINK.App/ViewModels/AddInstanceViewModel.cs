using System.Collections.ObjectModel;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
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

    public ObservableCollection<string> LogLines { get; } = [];

    public override string Title => "Add Instance";

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _dataPath = string.Empty;

    [ObservableProperty]
    private bool _isCopyChecked = true;

    [ObservableProperty]
    private bool _isMoveChecked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand), nameof(CreateInstanceCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Worker count for parallel hashing (1..ProcessorCount), injected by the
    /// caller (<see cref="MainViewModel.OpenAddInstanceAsync"/>) from settings.
    /// </summary>
    [ObservableProperty]
    private int _threadCount = Environment.ProcessorCount;

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
    private async Task CreateInstanceAsync()
    {
        IsBusy = true;
        var log = ProgressBridge.Create<string>(LogLines.Add, batchSize: 100);
        var percent = ProgressBridge.Create<double>(p => Progress = p);

        try
        {
            var request = new AddInstanceRequest(
                InstanceName, DataPath, Mode, Math.Clamp(ThreadCount, 1, Environment.ProcessorCount));
            var result = await _service.CreateInstanceAsync(request, log, percent);

            if (result.Success)
            {
                RequestClose();
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