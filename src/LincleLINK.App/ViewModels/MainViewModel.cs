using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Services;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _repository;
    private readonly StatusService _statusService;
    private readonly IDialogService _dialogs;
    private readonly IAppDialogHost _dialogHost;
    private readonly IThemeManager _themeManager;
    private readonly Func<AddInstanceViewModel> _addInstanceFactory;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAddInstanceCommand), nameof(DeleteInstanceCommand))]
    private InstanceListEntry? _selectedInstance;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAddInstanceCommand), nameof(DeleteInstanceCommand))]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAddInstanceCommand), nameof(DeleteInstanceCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _dbSize = string.Empty;

    [ObservableProperty]
    private string _savings = string.Empty;

    [ObservableProperty]
    private string _freeSpace = string.Empty;

    public MainViewModel(
        InstanceService instanceService,
        IInstanceRepository repository,
        StatusService statusService,
        IDialogService dialogs,
        IAppDialogHost dialogHost,
        IThemeManager themeManager,
        Func<AddInstanceViewModel> addInstanceFactory)
    {
        _instanceService = instanceService;
        _repository = repository;
        _statusService = statusService;
        _dialogs = dialogs;
        _dialogHost = dialogHost;
        _themeManager = themeManager;
        _addInstanceFactory = addInstanceFactory;
    }

    public async Task InitializeAsync()
    {
        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenAddInstance))]
    private async Task OpenAddInstanceAsync()
    {
        IsBusy = true;
        try
        {
            await _dialogHost.ShowDialogAsync(_addInstanceFactory());
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteInstance))]
    private async Task DeleteInstanceAsync()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        var result = await _instanceService.DeleteInstanceAsync(SelectedInstance.InstanceName);
        if (result.Deleted)
        {
            LogLines.Add($"Instance {SelectedInstance.InstanceName} deleted");
        }

        if (SelectedInstance is not null)
        {
            SelectedInstance = null;
        }

        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    private bool CanOpenAddInstance() => !IsBusy;

    private bool CanDeleteInstance() => !IsBusy && SelectedInstance is not null;

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeManager.Apply(value);
        LogLines.Add(value ? "Dark mode enabled" : "Dark mode disabled");
    }

    public async Task RefreshInstancesAsync()
    {
        var all = await _repository.GetAllAsync();
        var selectedName = SelectedInstance?.InstanceName;

        Instances.Clear();
        foreach (var instance in all)
        {
            Instances.Add(InstanceListEntry.From(instance));
        }

        if (selectedName is not null)
        {
            SelectedInstance = Instances.FirstOrDefault(i =>
                string.Equals(i.InstanceName, selectedName, StringComparison.OrdinalIgnoreCase));
        }

        LogLines.Add("Instance list updated.");
    }

    public async Task RefreshStatusAsync()
    {
        var summary = await _statusService.GetSummaryAsync();
        DbSize = summary.DbSizeString;
        Savings = summary.SavingsString;
        FreeSpace = summary.FreeSpaceString;
    }
}
