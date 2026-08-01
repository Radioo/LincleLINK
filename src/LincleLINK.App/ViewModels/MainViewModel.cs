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
    private readonly LinkingService _linkingService;
    private readonly UnusedFilesService _unusedFilesService;
    private readonly LegacyImporter _legacyImporter;
    private readonly IInstanceRepository _repository;
    private readonly StatusService _statusService;
    private readonly IDialogService _dialogs;
    private readonly IAppDialogHost _dialogHost;
    private readonly IThemeManager _themeManager;
    private readonly Func<AddInstanceViewModel> _addInstanceFactory;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand))]
    private InstanceListEntry? _selectedInstance;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand),
        nameof(CheckUnusedCommand),
        nameof(ImportLegacyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _dbSize = string.Empty;

    [ObservableProperty]
    private string _savings = string.Empty;

    [ObservableProperty]
    private string _freeSpace = string.Empty;

    public MainViewModel(
        InstanceService instanceService,
        LinkingService linkingService,
        UnusedFilesService unusedFilesService,
        LegacyImporter legacyImporter,
        IInstanceRepository repository,
        StatusService statusService,
        IDialogService dialogs,
        IAppDialogHost dialogHost,
        IThemeManager themeManager,
        Func<AddInstanceViewModel> addInstanceFactory)
    {
        _instanceService = instanceService;
        _linkingService = linkingService;
        _unusedFilesService = unusedFilesService;
        _legacyImporter = legacyImporter;
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

    [RelayCommand(CanExecute = nameof(CanLinkFiles))]
    private async Task LinkFilesAsync()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        await RunOperationAsync(async (log, percent) =>
        {
            var result = await _linkingService.LinkInstanceAsync(SelectedInstance!.InstanceName, log, percent);
            if (result.Cancelled)
            {
                LogLines.Add("Link operation aborted.");
            }
            else if (result.Error is not null)
            {
                LogLines.Add(result.Error);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCopyHashed))]
    private async Task CopyHashedAsync()
    {
        if (SelectedInstance is null)
        {
            return;
        }

        await RunOperationAsync(async (log, percent) =>
        {
            var result = await _linkingService.CopyHashedFilesAsync(SelectedInstance!.InstanceName, log, percent);
            if (result.Cancelled)
            {
                LogLines.Add("Copy operation aborted.");
            }
            else if (result.Error is not null)
            {
                LogLines.Add(result.Error);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheckUnused))]
    private async Task CheckUnusedAsync()
    {
        await RunOperationAsync(async (log, _) =>
        {
            var result = await _unusedFilesService.CheckAndDeleteAsync(log);
            if (result.Cancelled)
            {
                LogLines.Add("Unused files deletion aborted.");
            }
        });

        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    [RelayCommand(CanExecute = nameof(CanImportLegacy))]
    private async Task ImportLegacyAsync()
    {
        var path = await _dialogs.PickOpenFileAsync("Select legacy DBInfo.xml", "Legacy DBInfo|*.xml");
        if (path is null)
        {
            LogLines.Add("Import operation aborted.");
            return;
        }

        await RunOperationAsync(async (log, _) =>
        {
            var result = await _legacyImporter.ImportAsync(path);
            foreach (var name in result.Imported)
            {
                log.Report($"Instance {name} imported.");
            }

            foreach (var name in result.SkippedExisting)
            {
                log.Report($"Instance {name} already exists. Not importing.");
            }

            log.Report("Importing finished");
        });

        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    private async Task RunOperationAsync(
        Func<IProgress<string>, IProgress<double>, Task> operation)
    {
        IsBusy = true;
        try
        {
            var log = new Progress<string>(LogLines.Add);
            var percent = new Progress<double>(p => Progress = p);
            await operation(log, percent);
        }
        catch (Exception ex)
        {
            LogLines.Add(ex.Message);
        }
        finally
        {
            Progress = 0;
            IsBusy = false;
        }
    }

    private bool CanLinkFiles() => !IsBusy && SelectedInstance is not null;

    private bool CanCopyHashed() => !IsBusy && SelectedInstance is not null;

    private bool CanCheckUnused() => !IsBusy;

    private bool CanImportLegacy() => !IsBusy;

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
