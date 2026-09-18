using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Collections;
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
    private readonly Func<InstanceFilesViewModel> _instanceFilesFactory;
    private readonly Func<DuplicateInstanceViewModel> _duplicateInstanceFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DiagnosticLogOptions _logOptions;
    private readonly LogoCatalog _logoCatalog;
    private readonly IAppPaths _paths;
    private readonly IExceptionReporter _exceptionReporter;

    /// <summary>Logo key → index in the built-in catalog, i.e. the supported-list order.</summary>
    private readonly Dictionary<string, int> _logoOrder;

    public ObservableCollection<InstanceListEntry> Instances { get; } = [];

    /// <summary>The library grid's view of <see cref="Instances"/> after the filter box.</summary>
    public ObservableCollection<InstanceListEntry> FilteredInstances { get; } = [];

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
        nameof(CopyHashedCommand),
        nameof(BrowseFilesCommand),
        nameof(UpdateInstanceCommand),
        nameof(OpenDuplicateCommand))]
    private InstanceListEntry? _selectedInstance;

    partial void OnSelectedInstanceChanged(InstanceListEntry? value)
    {
        _ = LoadUniqueSizeAsync(value);
        SelectedLogoUri = value?.LogoUri;
    }

    [ObservableProperty]
    private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    /// <summary>Inspector figure: bytes referenced by the selection and no other entry.</summary>
    [ObservableProperty]
    private string _selectedUniqueSizeText = string.Empty;

    [ObservableProperty]
    private bool _isGridView;

    [ObservableProperty]
    private string? _selectedLogoUri;

    public ObservableCollection<LogoEntry> AvailableLogos { get; } = [];

    [ObservableProperty]
    private bool _isLogoPickerOpen;

    partial void OnIsGridViewChanged(bool value) =>
        SaveSettings(viewMode: value ? LibraryViewMode.Grid : LibraryViewMode.List);

    // ── slide-over add flow ────────────────────────────────────────────────

    [ObservableProperty]
    private AddInstanceViewModel? _addInstance;

    [ObservableProperty]
    private bool _isAddPanelOpen;

    // ── files dialog (plan 16 D4) ──────────────────────────────────────────

    /// <summary>The open files dialog, or null. The shell shows it as an in-window overlay.</summary>
    [ObservableProperty]
    private InstanceFilesViewModel? _instanceFiles;

    /// <summary>The open duplicate dialog (plan 16 D3), or null.</summary>
    [ObservableProperty]
    private DuplicateInstanceViewModel? _duplicateDialog;

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
        OpenLogFolderCommand.NotifyCanExecuteChanged();

        if (_seedingSaveLogToFile)
        {
            // Program.Main already seeded the live switch from the same settings;
            // a VM seed must not re-touch the process-global switch or rewrite the
            // settings file with identical content.
            _seedingSaveLogToFile = false;
            return;
        }

        SaveSettings(saveLogToFile: value);
        FileLoggingSwitch.Enabled = value;

        if (value)
        {
            Directory.CreateDirectory(LogDirectory);
            SerilogPipeline.WriteHeader();
            _logger.LogInformation("{Prefix} {Directory}", LogMessages.DiagnosticLogEnabledPrefix, LogDirectory);
        }
        else
        {
            _logger.LogInformation(LogMessages.DiagnosticLogDisabled);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenLogFolder))]
    private void OpenLogFolder()
    {
        try
        {
            FolderOpener.Open(LogDirectory);
        }
        catch (Exception ex)
        {
            // A missing or broken platform launcher (e.g. xdg-open on a minimal
            // Linux install) must degrade to a warning, never crash the app
            // through the diagnostics button.
            _logger.LogWarning(ex, "Could not open the log folder {LogDirectory}", LogDirectory);
        }
    }

    private bool CanOpenLogFolder() => Directory.Exists(LogDirectory);

    // ── activity bar ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(OpenAddInstanceCommand),
        nameof(DeleteInstanceCommand),
        nameof(LinkFilesCommand),
        nameof(CopyHashedCommand),
        nameof(BrowseFilesCommand),
        nameof(UpdateInstanceCommand),
        nameof(OpenDuplicateCommand),
        nameof(CheckUnusedCommand),
        nameof(ImportLegacyCommand),
        nameof(ChangeDataDirectoryCommand),
        nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    private double _progress;

    /// <summary>The running operation, e.g. "Deploy to folder"; empty when idle.</summary>
    [ObservableProperty]
    private string _operationName = string.Empty;

    /// <summary>What the running operation is doing right now; empty when idle.</summary>
    [ObservableProperty]
    private string _operationStatus = string.Empty;

    /// <summary>
    /// Everything that takes time shows progress (CLAUDE.md). An operation has
    /// nothing to count before its first percent (reading a torrent, listing a
    /// folder, asking the database) and after its last (one big save). A bar stuck
    /// at 0% or 100% looks hung, so it runs indeterminate and the status says why.
    /// </summary>
    public bool IsProgressIndeterminate => Progress <= 0 || Progress >= 100;

    [RelayCommand]
    private void ToggleViewMode() => IsGridView = !IsGridView;

    [RelayCommand]
    private void OpenLogoPicker()
    {
        AvailableLogos.Clear();
        foreach (var logo in _logoCatalog.AllLogos)
        {
            AvailableLogos.Add(logo);
        }

        IsLogoPickerOpen = true;
    }

    [RelayCommand]
    private void CloseLogoPicker() => IsLogoPickerOpen = false;

    [RelayCommand]
    private async Task SetCustomLogo(LogoEntry? logo)
    {
        IsLogoPickerOpen = false;

        if (SelectedInstance is null) return;

        try
        {
            var name = SelectedInstance.InstanceName;

            if (logo is null)
            {
                // reset to auto; the file delete doesn't belong on the UI thread
                var dataDirectory = _paths.DataDirectory;
                await Task.Run(() => LogoCatalog.DeleteCustomLogo(dataDirectory, name.ToLowerInvariant()));
                await _repository.SetCustomLogoAsync(name, null);
            }
            else
            {
                await _repository.SetCustomLogoAsync(name, logo.LogoKey);
            }

            await RefreshInstancesAsync();
        }
        catch (Exception ex)
        {
            // A locked custom-logo file or a failed DB write must not take the
            // process down on the UI context; degrade and log instead.
            _logger.LogError(ex, "Could not change the custom logo for '{InstanceName}'", SelectedInstance?.InstanceName);
        }
    }

    [RelayCommand]
    private async Task SetCustomImageAsync()
    {
        IsLogoPickerOpen = false;

        if (SelectedInstance is null) return;

        try
        {
            var name = SelectedInstance.InstanceName;
            var picked = await _dialogs.PickOpenFileAsync("Select image", new Core.Abstractions.Dialogs.FileType("Images", ["*.png", "*.jpg", "*.jpeg"]));
            if (picked is null) return;

            // A file copy, possibly from a slow drive: not on the UI thread.
            var dataDirectory = _paths.DataDirectory;
            await Task.Run(() => LogoCatalog.SaveCustomLogo(dataDirectory, name.ToLowerInvariant(), picked));
            await _repository.SetCustomLogoAsync(name, "custom");

            await RefreshInstancesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not set the custom image for '{InstanceName}'", SelectedInstance?.InstanceName);
        }
    }

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
        Func<InstanceFilesViewModel> instanceFilesFactory,
        Func<DuplicateInstanceViewModel> duplicateInstanceFactory,
        ILogger<MainViewModel> logger,
        DiagnosticLogOptions logOptions,
        LogoCatalog logoCatalog,
        IAppPaths paths,
        IExceptionReporter exceptionReporter)
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
        _instanceFilesFactory = instanceFilesFactory;
        _duplicateInstanceFactory = duplicateInstanceFactory;
        _logger = logger;
        _logOptions = logOptions;
        _logoCatalog = logoCatalog;
        _paths = paths;
        _exceptionReporter = exceptionReporter;

        _logoOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < logoCatalog.AllLogos.Count; i++)
        {
            _logoOrder[logoCatalog.AllLogos[i].LogoKey] = i;
        }

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

        var settings = _settingsStore.Load();
        if (settings is not null)
        {
            // Seed the library view mode from persisted settings.
            IsGridView = settings.ViewMode == LibraryViewMode.Grid;

            // Seed the diagnostics toggle from persisted settings without the
            // user-flip side effects (Program.Main already set the live switch).
            // The generated setter is equality-guarded: when the persisted value
            // equals the current field (e.g. false->false) OnSaveLogToFileChanged
            // is never invoked, so the flag must clear unconditionally or the
            // first user flip would be mistaken for a seed and never enable the
            // live switch.
            _seedingSaveLogToFile = true;
            try
            {
                SaveLogToFile = settings.SaveLogToFile;
            }
            finally
            {
                _seedingSaveLogToFile = false;
            }
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
        AddInstance = null;
        IsAddPanelOpen = false;

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
            _logger.LogWarning(ex, "Could not refresh the library after adding an instance");
        }
    }

    // ── files dialog (plan 16 D4) ─────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private Task BrowseFilesAsync() => OpenInstanceFilesAsync(updating: false);

    /// <summary>The same dialog as "Browse files", opened with the drop zone showing.</summary>
    [RelayCommand(CanExecute = nameof(CanBrowseFiles))]
    private Task UpdateInstanceAsync() => OpenInstanceFilesAsync(updating: true);

    private async Task OpenInstanceFilesAsync(bool updating)
    {
        if (InstanceFiles is not null)
        {
            return;
        }

        var vm = _instanceFilesFactory();
        vm.ThreadCount = ThreadCount;
        vm.IsUpdating = updating;
        vm.CloseRequested += OnInstanceFilesClosed;
        InstanceFiles = vm;
        try
        {
            await vm.LoadAsync(SelectedInstance!.InstanceName);
        }
        catch (Exception ex)
        {
            // An empty tree over a veil would look like an entry with no files.
            _logger.LogError(ex, "Could not load the files of '{InstanceName}'", vm.InstanceName);
            await _dialogs.ErrorAsync(ex.Message, "Browse files");
            OnInstanceFilesClosed(vm, EventArgs.Empty);
        }
    }

    private void OnInstanceFilesClosed(object? sender, EventArgs e)
    {
        if (sender is not InstanceFilesViewModel vm)
        {
            return;
        }

        vm.CloseRequested -= OnInstanceFilesClosed;
        InstanceFiles = null;

        if (vm.Applied)
        {
            // File count, size, unique size and the storage card all moved.
            _ = RefreshSafeAsync();
            _ = LoadUniqueSizeAsync(SelectedInstance);
        }
    }

    // ── duplicate (plan 16 D3) ────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDuplicateInstance))]
    private void OpenDuplicate()
    {
        if (DuplicateDialog is not null)
        {
            return;
        }

        var vm = _duplicateInstanceFactory();
        vm.CloseRequested += OnDuplicateInstanceClosed;
        vm.Start(SelectedInstance!.InstanceName);
        DuplicateDialog = vm;
    }

    private void OnDuplicateInstanceClosed(object? sender, EventArgs e)
    {
        if (sender is not DuplicateInstanceViewModel vm)
        {
            return;
        }

        vm.CloseRequested -= OnDuplicateInstanceClosed;
        DuplicateDialog = null;

        if (vm.CreatedName is { } createdName)
        {
            _ = ShowDuplicateAsync(vm.SourceName, createdName);
        }
    }

    private async Task ShowDuplicateAsync(string sourceName, string createdName)
    {
        try
        {
            // A custom image lives in a file keyed by the entry name, so the copy
            // needs its own file or it would show no logo. A file copy, so not here
            // on the UI thread.
            var dataDirectory = _paths.DataDirectory;
            await Task.Run(() => LogoCatalog.CopyCustomLogo(
                dataDirectory, sourceName.ToLowerInvariant(), createdName.ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            // The copy still exists and works; it only falls back to no image.
            _logger.LogWarning(ex, "Could not copy the custom logo to '{InstanceName}'", createdName);
        }

        try
        {
            await RefreshAllAsync();
            SelectedInstance = FilteredInstances.FirstOrDefault(i =>
                string.Equals(i.InstanceName, createdName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show the duplicate '{InstanceName}' in the library", createdName);
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
            // A removed instance must not orphan its custom logo, or a future
            // same-named instance would inherit the wrong image.
            try
            {
                var dataDirectory = _paths.DataDirectory;
                await Task.Run(() => LogoCatalog.DeleteCustomLogo(dataDirectory, instanceName.ToLowerInvariant()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove the custom logo for {InstanceName}", instanceName);
            }

            _logger.LogInformation("Removed {InstanceName} from the library (its files stay in storage)", instanceName);
        }
        else if (result.Cancelled)
        {
            _logger.LogInformation("Removal of {InstanceName} cancelled", instanceName);
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
                _logger.LogInformation("Deploy of {InstanceName} cancelled", instanceName);
            }
            else if (result.Error is not null)
            {
                await _dialogs.ErrorAsync(result.Error, "Deploy to folder");
            }
            else if (result.Failed > 0)
            {
                await _dialogs.ErrorAsync(
                    $"Deployed {result.Linked} files; {result.Failed} failed.",
                    "Deploy to folder");
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
                _logger.LogInformation("Export of {InstanceName} cancelled", instanceName);
            }
            else if (result.Error is not null)
            {
                await _dialogs.ErrorAsync(result.Error, "Export storage files");
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
                _logger.LogInformation("Storage cleanup cancelled");
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
            _logger.LogInformation("Legacy import cancelled");
            return;
        }

        await RunOperationAsync("Import legacy DBInfo", async op =>
        {
            var result = await _legacyImporter.ImportAsync(path, op.Status, op.Percent, op.CancellationToken);
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

        // Stays until the operation has wound down: work already under way keeps
        // reporting for a moment, and those lines must not take this one back.
        OperationStatus = "Cancelling...";
    }

    private bool IsCancelling => _operationCts is { IsCancellationRequested: true };

    private void ShowStatus(string line)
    {
        if (!IsCancelling)
        {
            OperationStatus = line;
        }
    }

    public async Task RunOperationAsync(
        string operationName,
        Func<OperationContext, Task> operation)
    {
        IsBusy = true;
        OperationName = operationName;
        OperationStatus = $"{operationName}...";
        using var scope = _logger.BeginScope("Operation {Operation}", operationName);
        _logger.LogInformation("Starting operation {Operation}", operationName);
        var stopwatch = Stopwatch.StartNew();
        _taskbarProgress.BeginOperation();
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        CancelOperationCommand.NotifyCanExecuteChanged();
        try
        {
            var log = ProgressBridge.Create<string>(
                line =>
                {
                    _logger.LogDebug("Activity: {Line}", line);
                    ShowStatus(line);
                },
                batchSize: 100);

            // Per-file lines arrive by the thousand; batched, the bar shows the
            // latest one and the UI thread is never flooded.
            var status = ProgressBridge.Create<string>(ShowStatus, batchSize: 200);
            var percent = ProgressBridge.CreatePercent(p =>
            {
                Progress = p;
                _taskbarProgress.Report(p);
            });
            await operation(new OperationContext(log, percent, cts.Token) { Status = status });

            stopwatch.Stop();
            _logger.LogInformation(
                "Operation {Operation} completed in {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Operation {Operation} cancelled after {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Operation {Operation} failed after {ElapsedMs} ms",
                operationName, stopwatch.ElapsedMilliseconds);

            // Expected environmental failures (locked file, permission denied,
            // full disk: IOException and its subclasses, UnauthorizedAccessException)
            // stay a one-line friendly dialog. Anything else is unexpected and gets
            // the full crash-report window (issue #16 D5). IOException is a base
            // class: subtypes like PathTooLongException and FileLoadException also
            // match here, which is acceptable because they almost always surface
            // from environmental conditions on a user-configured path. Domain errors
            // never reach here: they are returned via the operation result and
            // already shown with ErrorAsync by the caller.
            if (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ErrorAsync(ex.Message, operationName);
            }
            else
            {
                _exceptionReporter.ReportUnexpected(ex);
            }
        }
        finally
        {
            _operationCts = null;
            Progress = 0;
            OperationName = string.Empty;
            OperationStatus = string.Empty;
            IsBusy = false;
            _taskbarProgress.EndOperation();
        }
    }

    // Distinct names required by the [RelayCommand(CanExecute=nameof(...))] source
    // generator; bodies delegate to two shared gates.
    private bool CanLinkFiles() => CanOperateWithSelection();
    private bool CanCopyHashed() => CanOperateWithSelection();
    private bool CanDeleteInstance() => CanOperateWithSelection();
    private bool CanBrowseFiles() => CanOperateWithSelection();
    private bool CanDuplicateInstance() => CanOperateWithSelection();
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
    private void SaveSettings(AppTheme? theme = null, int? threads = null, string? dataDirectory = null, LibraryViewMode? viewMode = null, bool? saveLogToFile = null)
    {
        var current = _settingsStore.Load();
        _settingsStore.Save(new AppSettings(
            theme ?? current.Theme,
            dataDirectory ?? current.DataDirectory,
            threads ?? current.HashThreadCount,
            viewMode ?? current.ViewMode,
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

        // Resolving a custom image asks the disk whether its file exists, once per
        // entry that has one; with the sorting it runs off the UI thread, which
        // only swaps the finished list in.
        var ordered = await Task.Run(() => all
            .OrderBy(LogoSortTier)
            .ThenBy(LogoCatalogIndex)
            .ThenBy(e => e.InstanceName, NaturalStringComparer.Instance)
            .Select(summary => summary with { LogoUri = ResolveLogoPath(summary) })
            .ToList());
        var selectedName = SelectedInstance?.InstanceName;

        Instances.Clear();
        foreach (var summary in ordered)
        {
            Instances.Add(summary);
        }

        ApplyFilter();

        if (selectedName is not null)
        {
            SelectedInstance = FilteredInstances.FirstOrDefault(i =>
                string.Equals(i.InstanceName, selectedName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The logo key an entry is shown with (custom image, picked logo, or the
    /// auto-detected one), matching <see cref="ResolveLogoPath"/>.
    /// </summary>
    private static string? EffectiveLogoKey(InstanceListEntry entry)
    {
        if (entry.CustomLogoSource == "custom")
        {
            return null;
        }

        if (entry.CustomLogoSource is { } customKey)
        {
            return customKey;
        }

        return entry.DetectedGame?.LogoKey;
    }

    /// <summary>0 for entries whose logo is in the built-in catalog, 1 otherwise.</summary>
    private int LogoSortTier(InstanceListEntry entry)
        => EffectiveLogoKey(entry) is { } key && _logoOrder.ContainsKey(key) ? 0 : 1;

    /// <summary>Index of the entry's logo in the built-in catalog (int.MaxValue when unknown).</summary>
    private int LogoCatalogIndex(InstanceListEntry entry)
    {
        if (EffectiveLogoKey(entry) is { } key && _logoOrder.TryGetValue(key, out var index))
        {
            return index;
        }

        return int.MaxValue;
    }

    private string? ResolveLogoPath(InstanceListEntry entry)
    {
        var key = entry.CustomLogoSource;
        if (key == "custom")
        {
            var file = LogoCatalog.GetCustomLogoFilePath(_paths.DataDirectory, entry.InstanceName.ToLowerInvariant());
            if (file is not null) return file;
            return null;
        }

        if (key is not null)
        {
            return _logoCatalog.GetLogoPath(key);
        }

        var detected = entry.DetectedGame?.LogoKey;
        if (detected is not null)
        {
            return _logoCatalog.GetLogoPath(detected);
        }

        return null;
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
        catch (Exception ex)
        {
            // The figure is informational; never let it break selection.
            _logger.LogDebug(ex, "Could not compute the unique size for {InstanceName}", entry.InstanceName);
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
            _logger.LogWarning(ex, "Could not refresh status");
        }
    }
}
