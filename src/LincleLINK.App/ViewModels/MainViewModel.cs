using System.Collections.ObjectModel;
using System.Diagnostics;
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
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// Shell view model (plan 15): sidebar navigation, the Library page
/// (list + filter + inspector), the slide-over add flow, the Settings page,
/// and the activity bar that owns all operation feedback.
/// </summary>
public partial class MainViewModel : ViewModelBase, IOperationHost
{
    private readonly InstanceService _instanceService;
    private readonly LinkingService _linkingService;
    private readonly UnusedFilesService _unusedFilesService;
    private readonly LegacyImporter _legacyImporter;
    private readonly IInstanceRepository _repository;
    private readonly StatusService _statusService;
    private readonly IDialogService _dialogs;
    private readonly IThemeManager _themeManager;
    private readonly ISettingsStore _settingsStore;
    private readonly ITaskbarProgress _taskbarProgress;
    private readonly Func<AddInstanceViewModel> _addInstanceFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DiagnosticLogOptions _logOptions;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];

    /// <summary>The library grid's view of <see cref="Instances"/> after the filter box.</summary>
    public ObservableCollection<InstanceListEntry> FilteredInstances { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>The torrent pre-fill page (paths, wizard gates, commands).</summary>
    public TorrentCheckViewModel TorrentCheck { get; }

    // ── navigation ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private bool _isLibraryPage = true;

    [ObservableProperty]
    private bool _isTorrentPage;

    [ObservableProperty]
    private bool _isSettingsPage;

    partial void OnSelectedNavIndexChanged(int value)
    {
        IsLibraryPage = value == 0;
        IsTorrentPage = value == 1;
        IsSettingsPage = value == 2;
    }

    // ── library page ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand))]
    private InstanceListEntry? _selectedInstance;

    partial void OnSelectedInstanceChanged(InstanceListEntry? value) => _ = LoadUniqueSizeAsync(value);

    [ObservableProperty]
    private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Inspector figure: bytes referenced by the selection and no other entry.</summary>
    [ObservableProperty]
    private string _selectedUniqueSizeText = string.Empty;

    // ── slide-over add flow ────────────────────────────────────────────────

    [ObservableProperty]
    private AddInstanceViewModel? _addInstance;

    [ObservableProperty]
    private bool _isAddPanelOpen;

    // ── settings / status ──────────────────────────────────────────────────

    /// <summary>Worker count for hashing and storage cleanup (1..<see cref="MaxThreadCount"/>).</summary>
    [ObservableProperty]
    private int _threadCount = Environment.ProcessorCount;

    public int MaxThreadCount => Environment.ProcessorCount;

    [ObservableProperty]
    private string _dbSize = string.Empty;

    [ObservableProperty]
    private string _librarySize = string.Empty;

    [ObservableProperty]
    private string _savings = string.Empty;

    [ObservableProperty]
    private string _freeSpace = string.Empty;

    /// <summary>Storage as a share of the un-deduplicated library total, 0..100 (sidebar bar).</summary>
    [ObservableProperty]
    private double _storageSharePercent;

    /// <summary>
    /// Data directory shown on the Settings page. The active directory is frozen
    /// at boot (IAppPaths singleton, SQLite connection string), so a change here
    /// is persisted only and picked up on the next launch.
    /// </summary>
    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private bool _dataDirectoryChangePending;

    // ── diagnostics (issue #17 D2) ──────────────────────────────────────────

    /// <summary>Opt-in on-disk diagnostic log; toggling applies live, no restart.</summary>
    [ObservableProperty]
    private bool _saveLogToFile;

    /// <summary>True while InitializeAsync seeds the persisted value (no user flip side effects).</summary>
    private bool _seedingSaveLogToFile;

    /// <summary>Resolved log folder, shown on the settings page and used by <see cref="OpenLogFolderCommand"/>.</summary>
    public string LogDirectory => _logOptions.Directory;

    partial void OnSaveLogToFileChanged(bool value)
    {
        SaveSettings(saveLogToFile: value);
        FileLoggingSwitch.Enabled = value;
        OpenLogFolderCommand.NotifyCanExecuteChanged();

        if (_seedingSaveLogToFile)
        {
            _seedingSaveLogToFile = false;
            return;
        }

        if (value)
        {
            Directory.CreateDirectory(LogDirectory);
            SerilogPipeline.WriteHeader();
            AddLogLine($"{LogMessages.DiagnosticLogEnabledPrefix} {LogDirectory}");
        }
        else
        {
            AddLogLine(LogMessages.DiagnosticLogDisabled);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenLogFolder))]
    private void OpenLogFolder() => FolderOpener.Open(LogDirectory);

    private bool CanOpenLogFolder() => Directory.Exists(LogDirectory);

    // ── activity bar ───────────────────────────────────────────────────────

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

    /// <summary>Transient one-line status shown in the activity bar while busy.</summary>
    [ObservableProperty]
    private string _statusLine = string.Empty;

    /// <summary>Idle-state activity line: the last operation's outcome.</summary>
    [ObservableProperty]
    private string _lastOutcome = "Idle";

    [ObservableProperty]
    private bool _lastOutcomeIsWarning;

    /// <summary>True while the activity-log drawer above the bar is expanded.</summary>
    [ObservableProperty]
    private bool _isLogOpen;

    [RelayCommand]
    private void ToggleLog() => IsLogOpen = !IsLogOpen;

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
        IThemeManager themeManager,
        ISettingsStore settingsStore,
        ITaskbarProgress taskbarProgress,
        IHardLinkPreflight hardLinkPreflight,
        Func<AddInstanceViewModel> addInstanceFactory,
        ILogger<MainViewModel> logger,
        DiagnosticLogOptions logOptions)
    {
        _instanceService = instanceService;
        _linkingService = linkingService;
        _unusedFilesService = unusedFilesService;
        _legacyImporter = legacyImporter;
        _repository = repository;
        _statusService = statusService;
        _dialogs = dialogs;
        _themeManager = themeManager;
        _settingsStore = settingsStore;
        _taskbarProgress = taskbarProgress;
        _addInstanceFactory = addInstanceFactory;
        _logger = logger;
        _logOptions = logOptions;
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

        // Seed the diagnostics toggle from persisted settings without the user-flip
        // side effects (Program.Main already set the live switch and wrote the header).
        var persisted = _settingsStore.Load();
        if (persisted is not null)
        {
            _seedingSaveLogToFile = true;
            SaveLogToFile = persisted.SaveLogToFile;
        }

        await RefreshAllAsync();
    }

    // ── add flow (slide-over, plan 15 D4) ─────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanOpenAddInstance))]
    private void OpenAddInstance()
    {
        if (AddInstance is not null)
        {
            return;
        }

        var vm = _addInstanceFactory();
        vm.ThreadCount = ThreadCount;
        vm.CloseRequested += OnAddInstanceClosed;
        AddInstance = vm;
        IsAddPanelOpen = true;
    }

    private void OnAddInstanceClosed(object? sender, EventArgs e)
    {
        if (sender is not AddInstanceViewModel vm)
        {
            return;
        }

        vm.CloseRequested -= OnAddInstanceClosed;
        var succeeded = vm.CompletedSuccessfully;
        AddInstance = null;
        IsAddPanelOpen = false;

        if (succeeded)
        {
            ReportOutcome("✓ Added to library");
        }

        _ = RefreshSafeAsync();
    }

    private async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            // A transient storage failure right after the panel closes must not
            // become an unobserved task exception; degrade and log instead.
            AddLogLine($"Could not refresh the library: {ex.Message}");
        }
    }

    // ── library operations ────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDeleteInstance))]
    private async Task DeleteInstanceAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        var result = await _instanceService.DeleteInstanceAsync(instanceName);
        if (result.Deleted)
        {
            AddLogLine($"Removed {instanceName} from the library (its files stay in storage).");
            ReportOutcome($"✓ Removed {instanceName} from the library");
        }
        else if (result.Cancelled)
        {
            AddLogLine("Removal cancelled.");
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

        await RunOperationAsync("Deploy to folder", async op =>
        {
            var result = await _linkingService.LinkInstanceAsync(
                instanceName, op.Log, op.Percent, op.CancellationToken);
            if (result.Cancelled)
            {
                AddLogLine("Deploy cancelled.");
            }
            else if (result.Error is not null)
            {
                AddLogLine(result.Error);
            }
            else
            {
                ReportOutcome(
                    result.Failed > 0
                        ? $"⚠ Deployed {result.Linked} files; {result.Failed} failed - see log"
                        : $"✓ Deployed {result.Linked} files",
                    isWarning: result.Failed > 0);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCopyHashed))]
    private async Task CopyHashedAsync()
    {
        var instanceName = SelectedInstance!.InstanceName;

        await RunOperationAsync("Export storage files", async op =>
        {
            var result = await _linkingService.CopyHashedFilesAsync(
                instanceName, op.Log, op.Percent, op.Status, op.CancellationToken);
            if (result.Cancelled)
            {
                AddLogLine("Export cancelled.");
            }
            else if (result.Error is not null)
            {
                AddLogLine(result.Error);
            }
            else
            {
                ReportOutcome($"✓ Exported {result.Copied} files");
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheckUnused))]
    private async Task CheckUnusedAsync()
    {
        await RunOperationAsync("Clean up storage", async op =>
        {
            var result = await _unusedFilesService.CheckAndDeleteAsync(
                op.Log, op.CancellationToken, threadCount: ThreadCount, status: op.Status);
            if (result.Cancelled)
            {
                AddLogLine("Storage cleanup cancelled.");
            }
            else
            {
                ReportOutcome(result.Found == 0
                    ? "✓ Storage is clean"
                    : $"✓ Deleted {result.Deleted} files from storage ({SizeFormatter.Format(result.FoundBytes)} freed)");
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
            AddLogLine("Import cancelled.");
            return;
        }

        await RunOperationAsync("Import legacy DBInfo", async op =>
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
            ReportOutcome($"✓ Imported {result.Imported.Count} entries");
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
        AddLogLine($"Data directory set to {path}. Restart LincleLINK to apply.");

        // The active directory is frozen at boot, so a restart is the only way the
        // change takes effect - say so explicitly, not just via the inline note.
        await _dialogs.InfoAsync(
            $"The data directory is now set to {path}.\n\n" +
            "Restart LincleLINK to start using it - until then the app keeps " +
            "working with the current location. Your data is not moved or copied.",
            "Restart required");
    }

    // ── operation host ────────────────────────────────────────────────────

    /// <summary>Requests cancellation of the running operation (plan 14 D5).</summary>
    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        StatusLine = "Cancelling...";
    }

    public async Task RunOperationAsync(
        string operationName,
        Func<OperationContext, Task> operation)
    {
        IsBusy = true;
        using var scope = _logger.BeginScope("Operation {Operation}", operationName);
        _logger.LogInformation("Starting operation {Operation}", operationName);
        var stopwatch = Stopwatch.StartNew();
        _taskbarProgress.BeginOperation();
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        CancelOperationCommand.NotifyCanExecuteChanged();
        try
        {
            var log = ProgressBridge.Create<string>(AddLogLine, batchSize: 100);
            var status = ProgressBridge.Create<string>(line => StatusLine = line, batchSize: 200);
            var percent = ProgressBridge.Create<double>(p =>
            {
                Progress = p;
                _taskbarProgress.Report(p);
            });
            await operation(new OperationContext(log, status, percent, cts.Token));

            stopwatch.Stop();
            _logger.LogInformation(
                "Operation {Operation} completed in {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AddLogLine("Operation cancelled.");
            ReportOutcome("Operation cancelled");
            _logger.LogInformation(
                "Operation {Operation} cancelled after {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AddLogLine(ex.Message);
            ReportOutcome($"⚠ {ex.Message}", isWarning: true);
            _logger.LogError(
                ex,
                "Operation {Operation} failed after {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);
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

    /// <summary>
    /// Single choke point for user-visible activity lines (issue #17 D5): timestamps
    /// the line for the drawer and mirrors it into the diagnostic log at Debug.
    /// </summary>
    public void AddLogLine(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
        _logger.LogDebug("Activity: {Line}", line);
    }

    public void ReportOutcome(string message, bool isWarning = false)
    {
        LastOutcome = message;
        LastOutcomeIsWarning = isWarning;
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
        AddLogLine(theme switch
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

    // The torrent page's commands gate on host busy state, so re-query them when it
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
    private void SaveSettings(AppTheme? theme = null, int? threads = null, string? dataDirectory = null, bool? saveLogToFile = null)
    {
        var current = _settingsStore.Load();
        _settingsStore.Save(new AppSettings(
            theme ?? current.Theme,
            dataDirectory ?? current.DataDirectory,
            threads ?? current.HashThreadCount,
            saveLogToFile ?? current.SaveLogToFile));
    }

    /// <summary>Refreshes the library list and storage card together after an operation.</summary>
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

        ApplyFilter();

        if (selectedName is not null)
        {
            SelectedInstance = FilteredInstances.FirstOrDefault(i =>
                string.Equals(i.InstanceName, selectedName, StringComparison.OrdinalIgnoreCase));
        }

        AddLogLine(LogMessages.LibraryRefreshed);
    }

    private void ApplyFilter()
    {
        FilteredInstances.Clear();
        foreach (var entry in Instances)
        {
            if (string.IsNullOrWhiteSpace(FilterText)
                || entry.InstanceName.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                FilteredInstances.Add(entry);
            }
        }

        if (SelectedInstance is not null && !FilteredInstances.Contains(SelectedInstance))
        {
            SelectedInstance = null;
        }
    }

    private async Task LoadUniqueSizeAsync(InstanceListEntry? entry)
    {
        if (entry is null)
        {
            SelectedUniqueSizeText = string.Empty;
            return;
        }

        SelectedUniqueSizeText = "…";
        try
        {
            var size = await _repository.GetUniqueSizeAsync(entry.InstanceName);

            // Only publish if the selection has not moved on meanwhile.
            if (string.Equals(SelectedInstance?.InstanceName, entry.InstanceName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedUniqueSizeText = SizeFormatter.Format(size);
            }
        }
        catch (Exception)
        {
            // The figure is informational; never let it break selection.
            if (string.Equals(SelectedInstance?.InstanceName, entry.InstanceName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedUniqueSizeText = "-";
            }
        }
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var summary = await _statusService.GetSummaryAsync();
            DbSize = summary.DbSizeString;
            LibrarySize = summary.LibrarySizeString;
            Savings = summary.SavingsString;
            FreeSpace = summary.FreeSpaceString;
            StorageSharePercent = summary.StorageShare * 100;
        }
        catch (Exception ex)
        {
            // A transient drive-info failure (unplugged volume, statvfs error) must
            // not escape to the startup handler; degrade gracefully and leave the
            // last-known status fields in place.
            AddLogLine($"Could not refresh status: {ex.Message}");
        }
    }
}
