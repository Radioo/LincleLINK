using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IOperationHost
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
    private readonly ISettingsStore _settingsStore;
    private readonly Func<AddInstanceViewModel> _addInstanceFactory;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>The "Link to torrent" tab (paths, piece gates, commands).</summary>
    public TorrentCheckViewModel TorrentCheck { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand))]
    private InstanceListEntry? _selectedInstance;

    /// <summary>Worker count used while adding a new instance (1..<see cref="MaxThreadCount"/>).</summary>
    [ObservableProperty]
    private int _threadCount = Environment.ProcessorCount;

    public int MaxThreadCount => Environment.ProcessorCount;

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
    private double _progress;

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
        TorrentService torrentService,
        IInstanceRepository repository,
        StatusService statusService,
        IDialogService dialogs,
        IAppDialogHost dialogHost,
        IThemeManager themeManager,
        ISettingsStore settingsStore,
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
        _settingsStore = settingsStore;
        _addInstanceFactory = addInstanceFactory;
        TorrentCheck = new TorrentCheckViewModel(torrentService, dialogs, this);
    }

    private bool _initialized;

    public async Task InitializeAsync()
    {
        // Idempotent: InitializeAsync can be triggered from both App.axaml.cs (when
        // the main window is already visible for the first-run path) and
        // MainWindow.OnOpened, whose ordering is window-lifecycle-dependent.
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAllAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenAddInstance))]
    private async Task OpenAddInstanceAsync()
    {
        IsBusy = true;
        try
        {
            var dialog = _addInstanceFactory();
            dialog.ThreadCount = ThreadCount;
            await _dialogHost.ShowDialogAsync(dialog);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAllAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteInstance))]
    private async Task DeleteInstanceAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        var result = await _instanceService.DeleteInstanceAsync(instanceName);
        if (result.Deleted)
        {
            LogLines.Add($"Instance {instanceName} deleted");
        }

        SelectedInstance = null;

        await RefreshAllAsync();
    }

    private bool CanOpenAddInstance() => CanOperate();

    [RelayCommand(CanExecute = nameof(CanLinkFiles))]
    private async Task LinkFilesAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        await RunOperationAsync(async (log, percent) =>
        {
            var result = await _linkingService.LinkInstanceAsync(instanceName, log, percent);
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
        var instanceName = SelectedInstance!.InstanceName;

        await RunOperationAsync(async (log, percent) =>
        {
            var result = await _linkingService.CopyHashedFilesAsync(instanceName, log, percent);
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

        await RefreshAllAsync();
    }

    [RelayCommand(CanExecute = nameof(CanImportLegacy))]
    private async Task ImportLegacyAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(
            "Select legacy DBInfo.xml", new FileType("Legacy DBInfo", ["*.xml"]));
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

        await RefreshAllAsync();
    }

    public async Task RunOperationAsync(
        Func<IProgress<string>, IProgress<double>, Task> operation)
    {
        IsBusy = true;
        try
        {
            var log = ProgressBridge.Create<string>(LogLines.Add, batchSize: 100);
            var percent = ProgressBridge.Create<double>(p => Progress = p);
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

    // Distinct names required by the [RelayCommand(CanExecute=nameof(...))] source
    // generator; bodies delegate to two shared gates.
    private bool CanLinkFiles() => CanOperateWithSelection();
    private bool CanCopyHashed() => CanOperateWithSelection();
    private bool CanDeleteInstance() => CanOperateWithSelection();
    private bool CanCheckUnused() => CanOperate();
    private bool CanImportLegacy() => CanOperate();

    private bool CanOperate() => !IsBusy;

    private bool CanOperateWithSelection() => !IsBusy && SelectedInstance is not null;

    protected override void OnThemeChanged(bool dark)
    {
        _themeManager.Apply(dark);
        SaveSettings(theme: dark);
        LogLines.Add(dark ? "Dark mode enabled" : "Dark mode disabled");
    }

    partial void OnThreadCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, MaxThreadCount);
        if (clamped != value)
        {
            // Set the backing field directly (no re-entrant handler call), then save
            // the clamped value in the same pass.
            SetProperty(ref _threadCount, clamped);
        }

        SaveSettings(threads: clamped);
    }

    // The torrent tab's commands gate on host busy state, so re-query them when it
    // changes. [NotifyCanExecuteChangedFor] can't target commands on another type,
    // hence this explicit partial-method hook.
    partial void OnIsBusyChanged(bool value) => NotifyTorrentCheckCommands();

    private void NotifyTorrentCheckCommands()
    {
        TorrentCheck.BrowseTorrentFileCommand.NotifyCanExecuteChanged();
        TorrentCheck.BrowseTorrentDlPathCommand.NotifyCanExecuteChanged();
        TorrentCheck.CheckFilesCommand.NotifyCanExecuteChanged();
        TorrentCheck.CheckPiecesCommand.NotifyCanExecuteChanged();
        TorrentCheck.LinkToTorrentCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Persists a single setting change, preserving the other fields from the
    /// currently stored settings so startup seeding never clobbers them.
    /// </summary>
    private void SaveSettings(bool? theme = null, int? threads = null)
    {
        var current = _settingsStore.Load();
        _settingsStore.Save(new AppSettings(
            theme ?? current.IsDarkTheme,
            current.DataDirectory,
            threads ?? current.HashThreadCount));
    }

    /// <summary>Refreshes the instance list and status panel together after an operation.</summary>
    public async Task RefreshAllAsync()
    {
        await RefreshInstancesAsync();
        await RefreshStatusAsync();
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

        LogLines.Add(LogMessages.InstanceListUpdated);
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var summary = await _statusService.GetSummaryAsync();
            DbSize = summary.DbSizeString;
            Savings = summary.SavingsString;
            FreeSpace = summary.FreeSpaceString;
        }
        catch (Exception ex)
        {
            // A transient drive-info failure (unplugged volume, statvfs error) must
            // not escape to the startup handler; degrade gracefully and leave the
            // last-known status fields in place.
            LogLines.Add($"Could not refresh status: {ex.Message}");
        }
    }
}
