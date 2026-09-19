using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.App.ViewModels.FileTree;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// The files dialog of one library entry (plan 16 D4): browse its file tree, stage
/// Sources, exclusions and Removals against it, and apply them together. The tree
/// always shows the entry as the Pending changes would leave it.
/// </summary>
public partial class InstanceFilesViewModel : ViewModelBase
{
    private const string DialogTitle = "Browse files";

    private readonly IInstanceRepository _repository;
    private readonly IFileStore _store;
    private readonly IDialogService _dialogs;
    private readonly InstanceUpdateService _updates;
    private readonly ITaskbarProgress _taskbarProgress;
    private readonly ILogger<InstanceFilesViewModel> _logger;

    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cancelled when the dialog closes, so no scan or hash outlives it.</summary>
    private readonly CancellationTokenSource _closed = new();

    /// <summary>One scan at a time, so Sources join the list in drop order: the later one wins a shared path.</summary>
    private readonly SemaphoreSlim _scanTurn = new(1, 1);

    private Instance? _instance;
    private UpdatePlan? _plan;
    private PlanStats? _stats;
    private CancellationTokenSource? _applyCts;

    /// <summary>The running detection; a newer plan cancels it instead of letting it finish unseen.</summary>
    private CancellationTokenSource? _detection;

    /// <summary>What detection found for the current plan, as <see cref="DetectedVersionText"/> shows it.</summary>
    private VersionChange? _detected;

    private string? _lastDetectedKey;

    /// <summary>Bumped on every recompute so a slow Storage lookup can't publish a stale summary.</summary>
    private int _planVersion;

    public InstanceFilesViewModel(
        IInstanceRepository repository,
        IFileStore store,
        IDialogService dialogs,
        InstanceUpdateService updates,
        ITaskbarProgress taskbarProgress,
        ILogger<InstanceFilesViewModel> logger)
    {
        _repository = repository;
        _store = store;
        _dialogs = dialogs;
        _updates = updates;
        _taskbarProgress = taskbarProgress;
        _logger = logger;

        Tree.Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoTreeMatches));
        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSources));
    }

    public FileTreeViewModel Tree { get; } = new();

    /// <summary>Staged Sources in drop order; when two hold the same path the later one wins.</summary>
    public ObservableCollection<StagedSourceViewModel> Sources { get; } = [];

    /// <summary>What blocks Apply: a file and a folder claiming one path.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>Worker count for hashing, forwarded from the shell's setting.</summary>
    public int ThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>True once Pending changes were applied, so the shell knows to refresh.</summary>
    public bool Applied { get; private set; }

    [ObservableProperty]
    private string _instanceName = string.Empty;

    /// <summary>File count and size of the entry after Apply, shown under its name.</summary>
    [ObservableProperty]
    private string _summary = "Loading...";

    /// <summary>What Apply would do, in words; empty when nothing is staged.</summary>
    [ObservableProperty]
    private string _changesSummary = string.Empty;

    /// <summary>
    /// The game version detection finds for the result, e.g. "Detected version:
    /// 2026031800 -> 2026091700"; empty when there is nothing new.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedVersion))]
    private string _detectedVersionText = string.Empty;

    public bool HasDetectedVersion => DetectedVersionText.Length > 0;

    /// <summary>Save the detected version with Apply. Unticked, the entry keeps the one it has.</summary>
    [ObservableProperty]
    private bool _adoptDetectedVersion = true;

    /// <summary>Detection for the current plan is still running; Apply waits for its outcome.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Activity), nameof(HasActivity))]
    private bool _isDetecting;

    /// <summary>The entry's file list is being read from the database.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Activity), nameof(HasActivity), nameof(HasNoTreeMatches))]
    private bool _isLoading = true; // from the moment it exists: the shell shows the dialog before it starts the load

    /// <summary>The load finished and the entry holds no files and no folders. Never true while loading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTreeMatches))]
    private bool _isEntryEmpty;

    /// <summary>The entry has content, but the filter box or "changes only" hides all of it.</summary>
    public bool HasNoTreeMatches
        => !IsLoading && !IsEntryEmpty && Tree.Rows.Count == 0 && (Tree.ChangesOnly || Tree.Filter.Trim().Length > 0);

    /// <summary>The preview is being planned and its tree built; what is on screen is about to be replaced.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Activity), nameof(HasActivity))]
    private bool _isPlanning;

    /// <summary>Dropped or picked items that are still being walked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Activity), nameof(HasActivity))]
    private int _scansRunning;

    /// <summary>
    /// What the dialog is working on in the background, for the indeterminate bar
    /// above the tree; empty when idle. Everything that takes time shows progress
    /// (CLAUDE.md): hashing has its bar on the Source, Apply its own in the footer,
    /// and everything else, which has no steps to count, is named here.
    /// </summary>
    public string Activity
        => IsLoading ? "Loading the entry's files..."
            : ScansRunning > 0 ? "Scanning the dropped files..."
            : IsPlanning ? "Updating the preview..."
            : IsDetecting ? "Detecting the game version..."
            : string.Empty;

    public bool HasActivity => Activity.Length > 0;

    /// <summary>Once something is staged the drop zone shrinks to one line: its explanation has done its job.</summary>
    public bool HasSources => Sources.Count > 0;

    /// <summary>Label of the switch above the tree that hides what stays as it is, with the number of changes.</summary>
    [ObservableProperty]
    private string _changesOnlyLabel = "Show only changes";

    /// <summary>Whether the drop zone and source list show. "Update..." opens the dialog with it on.</summary>
    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _hasPendingChanges;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(CloseCommand), nameof(AddFolderCommand), nameof(AddFilesCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    private double _progress;

    /// <summary>
    /// Apply's bar has nothing to count before the first copy finishes (checking the
    /// changes) and after the last one (the database save). A bar stuck at 0% or
    /// 100% would look hung, so it runs indeterminate and the status line says why.
    /// </summary>
    public bool IsProgressIndeterminate => Progress <= 0 || Progress >= 100;

    [ObservableProperty]
    private string _statusLine = string.Empty;

    public async Task LoadAsync(string instanceName, CancellationToken ct = default)
    {
        InstanceName = instanceName;
        IsLoading = true;
        try
        {
            // The repository the app resolves runs off the UI thread; reading 150k
            // file rows takes a while, and the dialog says so meanwhile.
            var (instance, notFound) = await InstanceLookup.GetAsync(_repository, instanceName, ct);
            if (instance is null)
            {
                await _dialogs.ErrorAsync(notFound!, DialogTitle);
                RequestClose();
                return;
            }

            _instance = instance;
            await RecomputeAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── staging ───────────────────────────────────────────────────────────

    /// <summary>
    /// Something the entry holds today and that isn't already going away. A file
    /// that only a Source supplies is not in the entry; unticking keeps it out.
    /// </summary>
    public static bool CanRemove(FileTreeRow row)
        => !row.IsRemoved && (row.IsDirectory || row.Status is not (PlannedStatus.Added or PlannedStatus.Excluded));

    /// <summary>Content a Source brings that isn't already left out.</summary>
    public static bool CanExclude(FileTreeRow row)
        => !row.IsExcluded && (row.IsDirectory ? row.ChangeCount > 0 : row.Planned?.Source is not null);

    /// <summary>
    /// Stages the Removal of files and folders the entry holds today. What a Source
    /// brings to the same path is left out too: a Source wins over a Removal, and
    /// "remove" would otherwise do nothing visible to a file that is being replaced.
    /// </summary>
    public Task StageRemovalAsync(IEnumerable<FileTreeRow> rows)
    {
        foreach (var row in rows.Where(CanRemove))
        {
            _removed.Add(row.Path);
            if (CanExclude(row))
            {
                _excluded.Add(row.Path);
            }
        }

        return RecomputeAsync();
    }

    /// <summary>
    /// The Delete key on a mixed selection, as one staging step: what only a Source
    /// supplies is left out, what the entry holds is removed.
    /// </summary>
    public Task StageDeletionAsync(IEnumerable<FileTreeRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.IsAdded && CanExclude(row))
            {
                _excluded.Add(row.Path);
            }
            else if (!row.IsAdded && CanRemove(row))
            {
                _removed.Add(row.Path);
                if (CanExclude(row))
                {
                    _excluded.Add(row.Path);
                }
            }
        }

        return RecomputeAsync();
    }

    /// <summary>Unticks Source content: the files stay listed but out of the result.</summary>
    public Task StageExclusionAsync(IEnumerable<FileTreeRow> rows)
    {
        foreach (var row in rows.Where(CanExclude))
        {
            _excluded.Add(row.Path);
        }

        return RecomputeAsync();
    }

    /// <summary>
    /// Takes back every Removal and exclusion that covers a row or lies below it.
    /// A file inside a removed folder comes back together with that folder.
    /// </summary>
    public Task RestoreAsync(IEnumerable<FileTreeRow> rows)
    {
        foreach (var row in rows)
        {
            _removed.RemoveWhere(staged => Overlaps(staged, row.Path));
            _excluded.RemoveWhere(staged => Overlaps(staged, row.Path));
        }

        return RecomputeAsync();
    }

    private static bool Overlaps(string staged, string path)
    {
        var a = staged.ToUpperInvariant();
        var b = path.ToUpperInvariant();
        return PathNormalizer.IsWithin(a, b) || PathNormalizer.IsWithin(b, a);
    }

    /// <summary>
    /// Stages what the user dropped or picked as one Source. The path-only preview
    /// shows right after the scan; hashing then settles replaced versus identical.
    /// </summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths, string destination = "")
    {
        if (paths.Count == 0 || _instance is null)
        {
            return;
        }

        IsUpdating = true;
        StagedSourceViewModel? staged = null;
        try
        {
            // Walking a big dropped folder takes a while and there is no Source row
            // to carry a progress bar yet, so the dialog's activity line shows it.
            ScansRunning++;
            SourceScan scan;
            try
            {
                await _scanTurn.WaitAsync(_closed.Token);
                try
                {
                    scan = await _updates.ScanAsync(paths, _closed.Token);
                    _closed.Token.ThrowIfCancellationRequested();

                    var label = paths.Count == 1
                        ? Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]))
                        : $"{paths.Count} items";
                    staged = new StagedSourceViewModel(
                        label, scan, destination, _closed.Token, RemoveSource, () => _ = RecomputeAsync());
                    Sources.Add(staged);
                }
                finally
                {
                    _scanTurn.Release();
                }
            }
            finally
            {
                ScansRunning--;
            }

            await RecomputeAsync(expandChanges: true);

            var progress = ProgressBridge.CreatePercent(p => staged.HashProgress = p);
            var hashed = await _updates.HashAsync(scan.Source, ThreadCount, progress, staged.Cancellation.Token);
            staged.SetHashed(hashed);
            await RecomputeAsync();
        }
        catch (OperationCanceledException)
        {
            // The source was removed, or the dialog closed, while it was hashing.
        }
        catch (Exception ex)
        {
            // Drops start this without awaiting it, so nothing may escape: a Source
            // that can't be staged goes away again and the user hears why.
            _logger.LogError(ex, "Could not stage {Count} dropped paths", paths.Count);
            if (staged is not null)
            {
                RemoveSource(staged);
            }

            await _dialogs.ErrorAsync(ex.Message, "Update");
        }
    }

    /// <summary>
    /// Stages without waiting for the hashing, which can take minutes. A command
    /// that awaited it would keep its button disabled for that long.
    /// </summary>
    private void StartAdding(IReadOnlyList<string> paths) => _ = AddPathsAsync(paths);

    private void RemoveSource(StagedSourceViewModel source)
    {
        if (Sources.Remove(source))
        {
            _ = RecomputeAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task AddFolderAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Select the folder with the new files");
        if (folder is not null)
        {
            StartAdding([folder]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task AddFilesAsync() => StartAdding(await _dialogs.PickOpenFilesAsync("Select the new files"));

    [RelayCommand]
    private void StartUpdating() => IsUpdating = true;

    // ── preview ───────────────────────────────────────────────────────────

    /// <summary>
    /// Plans the entry as the Pending changes leave it and shows that. Planning and
    /// building the tree of a large entry take a third of a second and more, so both
    /// run on the thread pool and only the finished rows are swapped in here
    /// (CLAUDE.md: the UI never freezes). Never throws: several callers don't await it.
    /// </summary>
    private Task RecomputeAsync(bool expandChanges = false)
    {
        if (_instance is null || _closed.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var version = ++_planVersion;

        // What was detected belongs to the plan it was detected for.
        _detection?.Cancel();
        _detection = null;
        _detected = null;
        DetectedVersionText = string.Empty;

        IsPlanning = true;
        return RecomputeCoreAsync(
            _instance,
            Sources.Select(s => s.Source).ToList(),
            [.. _excluded],
            [.. _removed],
            Tree.CaptureView(),
            expandChanges,
            version);
    }

    private async Task RecomputeCoreAsync(
        Instance instance,
        List<UpdateSource> sources,
        List<string> excluded,
        List<string> removed,
        TreeViewState view,
        bool expandChanges,
        int version)
    {
        try
        {
            var (plan, prepared, stats) = await Task.Run(() =>
            {
                var computed = UpdatePlanner.Compute(instance, sources, excluded, removed);
                return (computed, FileTreeViewModel.Build(computed, view), PlanStats.Of(instance, computed));
            });

            // A newer staging step started meanwhile; its result is the one to show.
            if (version != _planVersion || _closed.IsCancellationRequested)
            {
                return;
            }

            _plan = plan;
            _stats = stats;
            Tree.Show(prepared);
            if (expandChanges)
            {
                Tree.ExpandChanges();
            }

            Summary = $"{stats.ResultingFiles} files, {SizeFormatter.Format(stats.ResultingBytes)}";

            Problems.Clear();
            foreach (var collision in plan.Collisions)
            {
                Problems.Add($"A file and a folder would both be named {collision.Path}. Remove that source or untick one of them.");
            }

            HasPendingChanges = prepared.HasChanges;
            ChangesSummary = Describe(stats, bytesNewToStorage: null);
            ChangesOnlyLabel = $"Show only changes ({stats.Changes})";
            if (!prepared.HasChanges)
            {
                // Nothing left to narrow down to; an empty tree would look like a fault.
                Tree.ChangesOnly = false;
            }
            IsEntryEmpty = plan.Files.Count == 0 && plan.Directories.All(d => d.Length == 0) && sources.Count == 0;

            if (plan.CanApply && stats.BringsContent)
            {
                _ = PublishStorageBytesAsync(plan, stats, version);
            }

            if (HasPendingChanges)
            {
                _detection = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                _ = PublishDetectedVersionAsync(instance, plan, sources, _detection, version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update the preview of '{InstanceName}'", InstanceName);
        }
        finally
        {
            if (version == _planVersion)
            {
                IsPlanning = false;
                ApplyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The numbers of a plan, counted once on the thread pool: a plan can hold 150k files.</summary>
    private sealed record PlanStats(
        int ResultingFiles,
        long ResultingBytes,
        long SizeDelta,
        IReadOnlyDictionary<PlannedStatus, int> Counts,
        long RemovedBytes,
        int RemovedDirectories)
    {
        public bool BringsContent => Count(PlannedStatus.Added) > 0 || Count(PlannedStatus.Replaced) > 0;

        /// <summary>Files the Pending changes add, replace, remove or may still replace.</summary>
        public int Changes
            => Count(PlannedStatus.Added) + Count(PlannedStatus.Replaced) + Count(PlannedStatus.Removed) + Count(PlannedStatus.Pending);

        public int Count(PlannedStatus status) => Counts.GetValueOrDefault(status);

        public static PlanStats Of(Instance instance, UpdatePlan plan)
        {
            var counts = new Dictionary<PlannedStatus, int>();
            var resultingFiles = 0;
            long resultingBytes = 0;
            long removedBytes = 0;
            foreach (var file in plan.Files)
            {
                counts[file.Status] = counts.GetValueOrDefault(file.Status) + 1;
                if (file.IsInResult)
                {
                    resultingFiles++;
                    resultingBytes += file.File.FileSize;
                }
                else if (file.Status == PlannedStatus.Removed)
                {
                    removedBytes += file.File.FileSize;
                }
            }

            return new PlanStats(
                resultingFiles,
                resultingBytes,
                resultingBytes - instance.FileList.Sum(f => f.FileSize),
                counts,
                removedBytes,
                plan.RemovedDirectories.Count);
        }
    }

    private PendingChanges SnapshotChanges()
        => new(Sources.Select(s => s.Source).ToList(), [.. _excluded], [.. _removed])
        {
            DetectedGame = AdoptDetectedVersion ? _detected?.Detected : null,
        };

    /// <summary>
    /// Runs game detection for the previewed result (plan 16 D10) and shows what it
    /// finds. Apply waits for it and saves exactly this, so the user has always
    /// seen, and could decline, the version an entry gets tagged with.
    /// </summary>
    private async Task PublishDetectedVersionAsync(
        Instance instance, UpdatePlan plan, List<UpdateSource> sources, CancellationTokenSource detection, int version)
    {
        IsDetecting = true;
        try
        {
            var change = await _updates.DetectVersionChangeAsync(instance, plan, sources, detection.Token);
            if (version != _planVersion || change is null)
            {
                return;
            }

            // A tick taken off one detected version says nothing about another one.
            var key = $"{change.Detected.GameCode}|{change.Detected.DateCode}";
            if (key != _lastDetectedKey)
            {
                _lastDetectedKey = key;
                AdoptDetectedVersion = true;
            }

            _detected = change;
            DetectedVersionText = Describe(change);
        }
        catch (OperationCanceledException)
        {
            // A newer plan took over, or the dialog closed.
        }
        catch (Exception ex)
        {
            // Informational, like the storage figure: the preview just stays without it.
            _logger.LogDebug(ex, "Could not detect the game version for the update of {InstanceName}", InstanceName);
        }
        finally
        {
            // Let go of the token source before disposing it, or the next plan
            // would cancel a disposed one.
            if (ReferenceEquals(_detection, detection))
            {
                _detection = null;
            }

            detection.Dispose();
            if (version == _planVersion)
            {
                IsDetecting = false;
            }
        }
    }

    private static string Describe(VersionChange change)
    {
        var detected = change.Detected;
        string text;
        if (change.Current is not { } current)
        {
            text = $"Detected game: {Name(detected)}";
        }
        else if (!string.Equals(current.GameCode, detected.GameCode, StringComparison.OrdinalIgnoreCase))
        {
            text = $"Detected game: {Name(detected)} (tagged as {Name(current)} now)";
        }
        else
        {
            text = $"Detected version: {current.DateCode ?? Name(current)} -> {detected.DateCode ?? Name(detected)}";
        }

        return change.FromDroppedFolder ? text + " (found next to the dropped folder)" : text;

        static string Name(GameVersionInfo game)
            => $"{game.DisplayTitle ?? game.GameTitle} {game.DateCode}".Trim();
    }

    /// <summary>One existence check per new hash, so it runs off the UI thread and joins the summary when ready.</summary>
    private async Task PublishStorageBytesAsync(UpdatePlan plan, PlanStats stats, int version)
    {
        try
        {
            var bytes = await Task.Run(() => _updates.GetBytesNewToStorage(plan));
            if (version == _planVersion)
            {
                ChangesSummary = Describe(stats, bytes);
            }
        }
        catch (Exception ex)
        {
            // The figure is informational; the summary just stays without it.
            _logger.LogDebug(ex, "Could not compute the bytes new to storage for {InstanceName}", InstanceName);
        }
    }

    private string Describe(PlanStats stats, long? bytesNewToStorage)
    {
        if (!HasPendingChanges)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        Count(PlannedStatus.Added, "added");
        Count(PlannedStatus.Replaced, "replaced");
        Count(PlannedStatus.Identical, "identical and skipped");
        Count(PlannedStatus.Pending, "still being checked");
        Count(PlannedStatus.Excluded, "unticked");
        var sentences = new List<string>();
        if (parts.Count > 0)
        {
            sentences.Add(string.Join(", ", parts) + ".");
        }

        if (stats.SizeDelta != 0)
        {
            sentences.Add(
                $"The entry {(stats.SizeDelta > 0 ? "grows" : "shrinks")} by {SizeFormatter.Format(Math.Abs(stats.SizeDelta))}.");
        }

        if (bytesNewToStorage is { } bytes)
        {
            sentences.Add($"{SizeFormatter.Format(bytes)} new to storage.");
        }
        else if (stats.BringsContent)
        {
            sentences.Add("Working out how much of it is new to storage...");
        }

        var removed = stats.Count(PlannedStatus.Removed);
        if (removed > 0 || stats.RemovedDirectories > 0)
        {
            sentences.Add(
                $"{removed} {(removed == 1 ? "file" : "files")}, " +
                $"{SizeFormatter.Format(stats.RemovedBytes)} will be removed from this entry. " +
                "They stay in storage until the next storage cleanup, and deployed folders keep them.");
        }

        return string.Join(" ", sentences);

        void Count(PlannedStatus status, string word)
        {
            var count = stats.Count(status);
            if (count > 0)
            {
                parts.Add($"{count} {word}");
            }
        }
    }

    // ── apply ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        _taskbarProgress.BeginOperation();
        using var cts = new CancellationTokenSource();
        _applyCts = cts;
        CancelApplyCommand.NotifyCanExecuteChanged();
        var log = ProgressBridge.Create<string>(line => AddLogLine(line, _logger), batchSize: 100);
        var status = ProgressBridge.Create<string>(
            line =>
            {
                if (!cts.IsCancellationRequested)
                {
                    StatusLine = line;
                }
            },
            batchSize: 200);
        var percent = ProgressBridge.CreatePercent(p =>
        {
            Progress = p;
            _taskbarProgress.Report(p);
        });

        try
        {
            var result = await _updates.ApplyAsync(InstanceName, SnapshotChanges(), log, percent, status, cts.Token);
            if (result.Success)
            {
                Applied = true;
                _closed.Cancel();
                RequestClose();
            }
            else if (result.Error is not null)
            {
                await _dialogs.ErrorAsync(result.Error, "Update");
            }
        }
        catch (OperationCanceledException)
        {
            AddLogLine("Operation cancelled.", _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update of '{InstanceName}' failed", InstanceName);
            await _dialogs.ErrorAsync(ex.Message, "Update");
        }
        finally
        {
            _applyCts = null;
            IsBusy = false;
            StatusLine = string.Empty;
            Progress = 0;
            _taskbarProgress.EndOperation();
            CancelApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApply()
        => !IsBusy && !IsLoading && !IsPlanning && !IsDetecting && HasPendingChanges && _plan is { CanApply: true };

    [RelayCommand(CanExecute = nameof(CanCancelApply))]
    private void CancelApply()
    {
        _applyCts?.Cancel();

        // Stays until Apply has wound down; later status lines don't take it back.
        StatusLine = "Cancelling...";
    }

    private bool CanCancelApply() => IsBusy && _applyCts is not null;

    private bool CanInteract() => !IsBusy;

    /// <summary>Opens the Storage folder that holds the content of a file row.</summary>
    [RelayCommand]
    private void RevealInStorage(FileTreeRow? row)
    {
        if (row?.File is not { HashedFileName.Length: > 0 } file)
        {
            return;
        }

        try
        {
            FolderOpener.Reveal(_store.GetPath(file.HashedFileName));
        }
        catch (Exception ex)
        {
            // A missing platform file manager must degrade to a warning (same
            // contract as the log-folder button).
            _logger.LogWarning(ex, "Could not reveal {HashedFileName} in storage", file.HashedFileName);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task CloseAsync()
    {
        if (HasPendingChanges || Sources.Count > 0)
        {
            var discard = await _dialogs.ConfirmAsync(
                "Close without applying? The pending changes will be discarded.", "Discard changes");
            if (!discard)
            {
                return;
            }
        }

        // Ends every scan and hash of this dialog; the Sources' tokens are linked to it.
        _closed.Cancel();
        RequestClose();
    }
}
