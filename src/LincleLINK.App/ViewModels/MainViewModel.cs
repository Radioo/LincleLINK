using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
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
    private readonly ITaskbarProgress _taskbarProgress;
    private readonly Func<AddInstanceViewModel> _addInstanceFactory;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>The torrent pre-fill tab (paths, wizard gates, commands).</summary>
    public TorrentCheckViewModel TorrentCheck { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand))]
    private InstanceListEntry? _selectedInstance;

    /// <summary>Worker count for hashing and storage cleanup (1..<see cref="MaxThreadCount"/>).</summary>
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
        nameof(ImportLegacyCommand),
        nameof(ChangeDataDirectoryCommand),
        nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    /// <summary>Transient one-line status shown above the progress bar (plan 14 D5).</summary>
    [ObservableProperty]
    private string _statusLine = string.Empty;

    [ObservableProperty]
    private string _dbSize = string.Empty;

    [ObservableProperty]
    private string _savings = string.Empty;

    [ObservableProperty]
    private string _freeSpace = string.Empty;

    /// <summary>
    /// Data directory shown on the Settings tab. The active directory is frozen at
    /// boot (IAppPaths singleton, SQLite connection string), so a change here is
    /// persisted only and picked up on the next launch.
    /// </summary>
    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private bool _dataDirectoryChangePending;

    private CancellationTokenSource? _operationCts;

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
        ITaskbarProgress taskbarProgress,
        IHardLinkPreflight hardLinkPreflight,
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
        _taskbarProgress = taskbarProgress;
        _addInstanceFactory = addInstanceFactory;
        TorrentCheck = new TorrentCheckViewModel(torrentService, dialogs, hardLinkPreflight, this);
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

    [RelayCommand(CanExecute = nameof(CanOpenAddInstance), AllowConcurrentExecutions = true)]
    private async Task OpenAddInstanceAsync()
    {
        // The dialog is modal, so the owner window is already non-interactive while
        // it is shown, and no concurrent interaction is possible. Holding IsBusy (or
        // letting the command's running state gate CanExecute) across the dialog and
        // the follow-up refresh is what strands the button when that refresh stalls;
        // with AllowConcurrentExecutions the button stays driven by IsBusy alone.
        var dialog = _addInstanceFactory();
        dialog.ThreadCount = ThreadCount;
        await _dialogHost.ShowDialogAsync(dialog);

        try
        {
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            // A transient storage failure right after the dialog must not surface to
            // the command and strand its button state; degrade and log instead.
            LogLines.Add($"Could not refresh the library: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteInstance))]
    private async Task DeleteInstanceAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        var result = await _instanceService.DeleteInstanceAsync(instanceName);
        if (result.Deleted)
        {
            LogLines.Add($"Removed {instanceName} from the library (its files stay in storage).");
        }
        else if (result.Cancelled)
        {
            LogLines.Add("Removal cancelled.");
            return;
        }

        SelectedInstance = null;

        await RefreshAllAsync();
    }

    private bool CanOpenAddInstance() => CanOperate();

    [RelayCommand(CanExecute = nameof(CanLinkFiles))]
    private async Task LinkFilesAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        await RunOperationAsync(async op =>
        {
            var result = await _linkingService.LinkInstanceAsync(
                instanceName, op.Log, op.Percent, op.CancellationToken);
            if (result.Cancelled)
            {
                LogLines.Add("Deploy cancelled.");
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

        await RunOperationAsync(async op =>
        {
            var result = await _linkingService.CopyHashedFilesAsync(
                instanceName, op.Log, op.Percent, op.Status, op.CancellationToken);
            if (result.Cancelled)
            {
                LogLines.Add("Export cancelled.");
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
        await RunOperationAsync(async op =>
        {
            var result = await _unusedFilesService.CheckAndDeleteAsync(
                op.Log, op.CancellationToken, threadCount: ThreadCount, status: op.Status);
            if (result.Cancelled)
            {
                LogLines.Add("Storage cleanup cancelled.");
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
            LogLines.Add("Import cancelled.");
            return;
        }

        await RunOperationAsync(async op =>
        {
            var result = await _legacyImporter.ImportAsync(path);
            foreach (var name in result.Imported)
            {
                op.Log.Report($"Imported {name} into the library.");
            }

            foreach (var name in result.SkippedExisting)
            {
                op.Log.Report($"{name} is already in the library. Not importing.");
            }

            op.Log.Report("Import finished.");
        });

        await RefreshAllAsync();
    }

    [RelayCommand(CanExecute = nameof(CanChangeDataDirectory))]
    private async Task ChangeDataDirectoryAsync()
    {
        var path = await _dialogs.PickFolderAsync("Select data directory", DataDirectory);
        if (path is null || string.Equals(path, DataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveSettings(dataDirectory: path);
        DataDirectory = path;
        DataDirectoryChangePending = true;
        LogLines.Add($"Data directory set to {path}. Restart LincleLINK to apply.");
    }

    /// <summary>Requests cancellation of the running operation (plan 14 D5).</summary>
    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        StatusLine = "Cancelling...";
    }

    public async Task RunOperationAsync(Func<OperationContext, Task> operation)
    {
        IsBusy = true;
        _taskbarProgress.BeginOperation();
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        CancelOperationCommand.NotifyCanExecuteChanged();
        try
        {
            var log = ProgressBridge.Create<string>(LogLines.Add, batchSize: 100);
            var status = ProgressBridge.Create<string>(line => StatusLine = line, batchSize: 200);
            var percent = ProgressBridge.Create<double>(p =>
            {
                Progress = p;
                _taskbarProgress.Report(p);
            });
            await operation(new OperationContext(log, status, percent, cts.Token));
        }
        catch (OperationCanceledException)
        {
            LogLines.Add("Operation cancelled.");
        }
        catch (Exception ex)
        {
            LogLines.Add(ex.Message);
        }
        finally
        {
            _operationCts = null;
            Progress = 0;
            StatusLine = string.Empty;
            IsBusy = false;
            _taskbarProgress.EndOperation();
        }
    }

    // Distinct names required by the [RelayCommand(CanExecute=nameof(...))] source
    // generator; bodies delegate to two shared gates.
    private bool CanLinkFiles() => CanOperateWithSelection();
    private bool CanCopyHashed() => CanOperateWithSelection();
    private bool CanDeleteInstance() => CanOperateWithSelection();
    private bool CanCheckUnused() => CanOperate();
    private bool CanImportLegacy() => CanOperate();
    private bool CanChangeDataDirectory() => CanOperate();
    private bool CanCancelOperation() => IsBusy && _operationCts is not null;

    private bool CanOperate() => !IsBusy;

    private bool CanOperateWithSelection() => !IsBusy && SelectedInstance is not null;

    protected override void OnThemeChanged(AppTheme theme)
    {
        _themeManager.Apply(theme);
        SaveSettings(theme: theme);
        LogLines.Add(theme switch
        {
            AppTheme.Dark => "Dark theme enabled",
            AppTheme.Light => "Light theme enabled",
            _ => "Following the system theme",
        });
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
    private void SaveSettings(AppTheme? theme = null, int? threads = null, string? dataDirectory = null)
    {
        var current = _settingsStore.Load();
        _settingsStore.Save(new AppSettings(
            theme ?? current.Theme,
            dataDirectory ?? current.DataDirectory,
            threads ?? current.HashThreadCount));
    }

    /// <summary>Refreshes the library list and status header together after an operation.</summary>
    public async Task RefreshAllAsync()
    {
        await RefreshInstancesAsync();
        await RefreshStatusAsync();
    }

    public async Task RefreshInstancesAsync()
    {
        var all = await _repository.GetSummariesAsync();
        var selectedName = SelectedInstance?.InstanceName;

        Instances.Clear();
        foreach (var summary in all)
        {
            Instances.Add(summary);
        }

        if (selectedName is not null)
        {
            SelectedInstance = Instances.FirstOrDefault(i =>
                string.Equals(i.InstanceName, selectedName, StringComparison.OrdinalIgnoreCase));
        }

        LogLines.Add(LogMessages.LibraryRefreshed);
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
