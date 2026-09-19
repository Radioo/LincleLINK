using System.Collections.Concurrent;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace LincleLINK.Core.Application;

/// <summary>Something in a Source that was left out: a skipped link or an unreadable file or folder.</summary>
/// <param name="Path">Path inside the Source, with '/' separators.</param>
public sealed record SourceIssue(string Path, string Message);

public sealed record SourceScan(UpdateSource Source, IReadOnlyList<SourceIssue> Issues);

/// <summary>Everything the user staged against an Instance. It applies together or not at all.</summary>
public sealed record PendingChanges(
    IReadOnlyList<UpdateSource> Sources,
    IReadOnlyCollection<string> ExcludedPaths,
    IReadOnlyCollection<string> RemovedPaths)
{
    /// <summary>
    /// The game version to tag the Instance with, as the preview showed it and the
    /// user accepted it; null keeps the tag it has. Apply doesn't detect again, so
    /// it can't save a version the user never saw.
    /// </summary>
    public GameVersionInfo? DetectedGame { get; init; }
}

/// <summary>A game version that detection finds for an Instance after its Pending changes.</summary>
/// <param name="Current">What the Instance is tagged with today; null when nothing.</param>
/// <param name="FromDroppedFolder">
/// Found next to a dropped folder on disk, not in the Instance's own files. Most
/// entries hold only a game's data folder, and the files that identify a version
/// sit beside it.
/// </param>
public sealed record VersionChange(GameVersionInfo? Current, GameVersionInfo Detected, bool FromDroppedFolder);

public sealed record ApplyUpdateResult(
    bool Success,
    string? Error,                // user-presentable failure message (null on cancel/success)
    int Added = 0,
    int Replaced = 0,
    int Removed = 0,
    long BytesAddedToStorage = 0)
{
    /// <summary>The user declined the low-disk warning.</summary>
    public bool Cancelled { get; init; }
}

/// <summary>
/// Update and Removal use case (plan 16 D5, D9): turns what the user dropped into a
/// Source, hashes it, and applies Pending changes. It only ever adds files to
/// Storage and rewrites the Instance's rows, so deployed folders are never touched.
/// </summary>
public sealed partial class InstanceUpdateService
{
    private const long LowDiskWiggleRoom = 100_000_000; // 100 MB, same margin as Add

    private readonly IFileSystem _fileSystem;
    private readonly IFileHasher _hasher;
    private readonly IFileStore _store;
    private readonly IInstanceRepository _repository;
    private readonly IDriveInfoProvider _driveInfo;
    private readonly IAppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IGameVersionDetector _detector;
    private readonly ILogger<InstanceUpdateService> _logger;

    public InstanceUpdateService(
        IFileSystem fileSystem,
        IFileHasher hasher,
        IFileStore store,
        IInstanceRepository repository,
        IDriveInfoProvider driveInfo,
        IAppPaths paths,
        IDialogService dialogs,
        IGameVersionDetector detector,
        ILogger<InstanceUpdateService> logger)
    {
        _detector = detector;
        _fileSystem = fileSystem;
        _hasher = hasher;
        _store = store;
        _repository = repository;
        _driveInfo = driveInfo;
        _paths = paths;
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>
    /// Walks what the user dropped or picked into one unhashed Source. One dropped
    /// folder becomes the Source's top folder; several items keep their own names.
    /// Directory links are skipped, not followed, and an unreadable folder is
    /// reported without ending the walk (plan 16 D5).
    /// </summary>
    public Task<SourceScan> ScanAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
        => Task.Run(() => Scan(paths, ct), ct);

    private SourceScan Scan(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var files = new List<SourceFile>();
        var directories = new List<string>();
        var issues = new List<SourceIssue>();
        var diskRoots = new List<string>();

        var isSingleFolder = paths.Count == 1 && _fileSystem.DirectoryExists(paths[0]);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (_fileSystem.DirectoryExists(path))
            {
                var relative = isSingleFolder ? string.Empty : name;
                if (relative.Length > 0)
                {
                    directories.Add(relative);
                }

                diskRoots.Add(path);
                Walk(path, relative, files, directories, issues, ct);
            }
            else if (_fileSystem.FileExists(path))
            {
                // A loose file's folder is no detection root: the detector also looks
                // into a start folder's subfolders, which for a file picked out of
                // Downloads are unrelated neighbours.
                files.Add(new SourceFile(string.Empty, name, _fileSystem.GetFileLength(path), null)
                {
                    FullPath = path,
                    LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(path),
                });
            }
            else
            {
                issues.Add(new SourceIssue(name, "Not found."));
            }
        }

        var source = new UpdateSource(string.Empty, files, directories)
        {
            TopFolder = isSingleFolder ? Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0])) : null,
            DiskRoots = diskRoots,
        };
        LogScanned(files.Count, directories.Count, issues.Count);
        return new SourceScan(source, issues);
    }

    private void Walk(
        string directory,
        string relative,
        List<SourceFile> files,
        List<string> directories,
        List<SourceIssue> issues,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<FileSystemEntry> entries;
        try
        {
            entries = _fileSystem.ListDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder was already listed by its parent; take it back out.
            directories.Remove(relative);
            issues.Add(new SourceIssue(relative, ex.Message));
            return;
        }

        foreach (var entry in entries)
        {
            var child = relative.Length == 0 ? entry.Name : $"{relative}/{entry.Name}";
            if (!entry.IsDirectory)
            {
                files.Add(new SourceFile(relative, entry.Name, entry.Length, null)
                {
                    FullPath = entry.FullPath,
                    LastWriteTimeUtc = entry.LastWriteTimeUtc,
                });
            }
            else if (entry.LinkTarget is not null)
            {
                issues.Add(new SourceIssue(child, $"Skipped, link to {entry.LinkTarget}"));
            }
            else
            {
                directories.Add(child);
                Walk(entry.FullPath, child, files, directories, issues, ct);
            }
        }
    }

    /// <summary>
    /// Hashes every file of a Source, giving each its Storage name (hash plus
    /// extension, as Add does). A file that can't be read is reported and dropped.
    /// </summary>
    public async Task<SourceScan> HashAsync(
        UpdateSource source,
        int? maxDegreeOfParallelism = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var hashed = new SourceFile?[source.Files.Count];
        var issues = new ConcurrentBag<(int Index, SourceIssue Issue)>();
        var done = 0;
        var maxDegree = Math.Clamp(maxDegreeOfParallelism ?? Environment.ProcessorCount, 1, Environment.ProcessorCount);

        await Parallel.ForEachAsync(
            source.Files.Select((file, index) => (file, index)),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    var hash = await _hasher.ComputeHashAsync(item.file.FullPath, token);
                    hashed[item.index] = item.file with { HashedFileName = hash + Path.GetExtension(item.file.FileName) };
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogHashFailed(item.file.FullPath, ex.Message);
                    var path = item.file.RelativePath.Length == 0
                        ? item.file.FileName
                        : $"{PathNormalizer.Canonicalize(item.file.RelativePath)}/{item.file.FileName}";
                    issues.Add((item.index, new SourceIssue(path, ex.Message)));
                }

                percent?.Report(Interlocked.Increment(ref done) * 100d / source.Files.Count);
            });

        return new SourceScan(
            source with { Files = hashed.Where(f => f is not null).Select(f => f!).ToList() },
            issues.OrderBy(i => i.Index).Select(i => i.Issue).ToList());
    }

    /// <summary>
    /// Bytes that applying <paramref name="plan"/> would add to Storage: content
    /// of added and replaced files that Storage doesn't hold yet, each hash once.
    /// </summary>
    public long GetBytesNewToStorage(UpdatePlan plan) => NewContent(plan).Sum(f => f.FileSize);

    private List<SourceFile> NewContent(UpdatePlan plan)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var content = new List<SourceFile>();
        foreach (var planned in plan.Files)
        {
            if (planned is { IsInResult: true, Source: { HashedFileName: { } hash } source }
                && planned.Status is PlannedStatus.Added or PlannedStatus.Replaced
                && seen.Add(hash)
                && !_store.Exists(hash))
            {
                content.Add(source);
            }
        }

        return content;
    }

    /// <summary>
    /// The game version of an Instance after its Pending changes (plan 16 D10), or
    /// null when detection finds nothing new. Finding nothing never clears a tag.
    ///
    /// An Instance that holds the files a game is identified by (its config, its
    /// game DLL) speaks for itself: it is detected as the plan leaves it, read
    /// through Storage and the Sources. Most entries hold only a game's data folder,
    /// and those files sit beside it on disk, so the dropped folders come next,
    /// latest Source first, and count only for the game the entry is tagged with.
    /// Changes that touch neither kind of file detect nothing.
    /// </summary>
    public Task<VersionChange?> DetectVersionChangeAsync(
        Instance instance,
        UpdatePlan plan,
        IReadOnlyList<UpdateSource> sources,
        CancellationToken ct = default)
        => Task.Run(() => DetectVersionChangeCoreAsync(instance, plan, sources, ct), ct);

    /// <summary>The same for Pending changes that have not been planned yet.</summary>
    public Task<VersionChange?> DetectVersionChangeAsync(
        Instance instance,
        PendingChanges changes,
        CancellationToken ct = default)
        => Task.Run(
            () => DetectVersionChangeCoreAsync(
                instance,
                UpdatePlanner.Compute(instance, changes.Sources, changes.ExcludedPaths, changes.RemovedPaths),
                changes.Sources,
                ct),
            ct);

    private async Task<VersionChange?> DetectVersionChangeCoreAsync(
        Instance instance,
        UpdatePlan plan,
        IReadOnlyList<UpdateSource> sources,
        CancellationToken ct)
    {
        var current = instance.DetectedGame;

        // Only a Source that ends up changing the entry says anything about it. One
        // that is fully unticked, or identical to what is there, is left alone.
        var contributing = plan.Files
            .Where(f => f.IsInResult && f.Status is PlannedStatus.Added or PlannedStatus.Replaced or PlannedStatus.Pending)
            .Select(f => f.SourceIndex)
            .ToHashSet();
        var identityTouched = plan.Files.Any(f => f.Status != PlannedStatus.Unchanged
                                                  && f.Status != PlannedStatus.Identical
                                                  && _detector.IsIdentityFile(f.File.FileName));
        var holdsIdentity = plan.Files.Any(f => f.IsInResult && _detector.IsIdentityFile(f.File.FileName));

        try
        {
            if (holdsIdentity && (identityTouched || contributing.Count > 0))
            {
                var planned = new PlannedInstanceFileSystem(plan, _store, _fileSystem);
                var inInstance = (await _detector.DetectAsync(planned, planned.Root, ct)).Info;

                // What the Instance says about itself settles it, unless all it has
                // is a game DLL without a version: a dropped folder may know more.
                if (inInstance?.DateCode is not null)
                {
                    return Differs(current, inInstance) ? new VersionChange(current, inInstance, false) : null;
                }

                if (inInstance is not null && current is null)
                {
                    return new VersionChange(null, inInstance, false);
                }
            }

            for (var index = sources.Count - 1; index >= 0; index--)
            {
                if (!contributing.Contains(index))
                {
                    continue;
                }

                foreach (var root in sources[index].DiskRoots)
                {
                    var nearby = (await _detector.DetectAsync(root, ct)).Info;
                    if (nearby is not null
                        && (current is null || string.Equals(current.GameCode, nearby.GameCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Differs(current, nearby) ? new VersionChange(current, nearby, true) : null;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best effort, as in Add: a failing detection must never sink an Update.
            _logger.LogWarning(ex, "Game detection failed for the update of '{InstanceName}'", instance.InstanceName);
        }

        return null;
    }

    /// <summary>
    /// Whether a detection tells more than the Instance's tag does. Game and date
    /// code decide; titles and logo keys are derived from them. A detection without
    /// a date code (a game DLL alone, whose name only hints at the game) never
    /// replaces a tag that has one.
    /// </summary>
    private static bool Differs(GameVersionInfo? current, GameVersionInfo detected)
    {
        if (current is null)
        {
            return true;
        }

        if (detected.DateCode is null)
        {
            return current.DateCode is null
                   && !string.Equals(current.GameCode, detected.GameCode, StringComparison.OrdinalIgnoreCase);
        }

        return !string.Equals(current.GameCode, detected.GameCode, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(current.DateCode, detected.DateCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies Pending changes (plan 16 D9): copies new content into Storage, then
    /// rewrites the Instance in one save. A cancel or failure before that save
    /// leaves the Instance as it was; a copy that was cut off leaves nothing in
    /// Storage, and whole files already copied stay there unreferenced until the
    /// next storage cleanup.
    ///
    /// Nothing here runs on the caller's thread except the low-disk question
    /// (CLAUDE.md: the UI never freezes). <paramref name="status"/> names every
    /// phase, and <paramref name="percent"/> reaches 100 before the save, which has
    /// no steps to count, so a caller can show that phase as indeterminate.
    /// </summary>
    public async Task<ApplyUpdateResult> ApplyAsync(
        string instanceName,
        PendingChanges changes,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        status?.Report($"Checking the changes to {instanceName}...");
        var prepared = await Task.Run(() => PrepareAsync(instanceName, changes, ct), ct);
        if (prepared.Failure is not null)
        {
            return Fail(instanceName, prepared.Failure);
        }

        // Back on the caller's context: the real dialog opens a window, which only
        // the UI thread may do.
        var bytes = prepared.NewContent.Sum(f => f.FileSize);
        if (bytes + LowDiskWiggleRoom > prepared.FreeSpace)
        {
            LogLowDisk(instanceName, prepared.FreeSpace, bytes);
            var proceed = await _dialogs.ConfirmAsync(
                $"The storage drive is low on disk space, do you want to continue? " +
                $"Free space: {SizeFormatter.Format(prepared.FreeSpace)}, " +
                $"size of files about to be copied into storage: {SizeFormatter.Format(bytes)}",
                "Low disk space");
            if (!proceed)
            {
                log?.Report("Operation cancelled.");
                return new ApplyUpdateResult(false, null) { Cancelled = true };
            }
        }

        return await Task.Run(
            () => CopyAndSaveAsync(prepared.Instance!, prepared.Plan!, prepared.NewContent, changes, log, percent, status, ct),
            ct);
    }

    private sealed record PreparedApply(
        string? Failure, Instance? Instance, UpdatePlan? Plan, List<SourceFile> NewContent, long FreeSpace);

    /// <summary>Everything Apply has to know before it may ask or copy: a database read, the plan, a stat per new file.</summary>
    private async Task<PreparedApply> PrepareAsync(string instanceName, PendingChanges changes, CancellationToken ct)
    {
        static PreparedApply Refuse(string why) => new(why, null, null, [], 0);

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, instanceName, ct);
        if (instance is null)
        {
            return Refuse(notFound!);
        }

        // The plan the user confirmed was a preview; the one that counts is computed
        // here, from the Instance as the database holds it now.
        var plan = UpdatePlanner.Compute(instance, changes.Sources, changes.ExcludedPaths, changes.RemovedPaths);
        if (plan.Collisions.Count > 0)
        {
            return Refuse(
                "A file and a folder would share one path: " + string.Join(", ", plan.Collisions.Select(c => c.Path)) + ".");
        }

        if (!plan.CanApply)
        {
            return Refuse("Some files have not been hashed yet.");
        }

        // A Destination is typed by the user. Deploy refuses rooted and '..' paths,
        // so an entry holding one could never be deployed again.
        var unsafePath = plan.Directories
            .Concat(plan.ResultingFiles.Select(f => Path.Combine(f.RelativePath, f.FileName)))
            .FirstOrDefault(p => !PathNormalizer.IsSafeRelativePath(p));
        if (unsafePath is not null)
        {
            return Refuse($"'{unsafePath}' is not a valid path inside an entry.");
        }

        ct.ThrowIfCancellationRequested();
        var (newContent, changedFile) = InspectNewContent(plan);
        if (changedFile is not null)
        {
            return Refuse(ChangedSinceHashed(changedFile));
        }

        return new PreparedApply(null, instance, plan, newContent, _driveInfo.GetAvailableFreeSpace(_paths.DbDirectory));
    }

    /// <summary>
    /// The content Apply has to copy, and the first Source file that no longer is
    /// what was hashed (null when all are). Content copied under a stale hash would
    /// sit in Storage under the wrong name for every later entry to pick up.
    /// </summary>
    private (List<SourceFile> NewContent, string? ChangedFile) InspectNewContent(UpdatePlan plan)
    {
        var newContent = NewContent(plan);
        foreach (var file in newContent)
        {
            if (!IsUnchanged(file))
            {
                return (newContent, file.FullPath);
            }
        }

        return (newContent, null);
    }

    /// <summary>Whether a Source file is still the one that was hashed: there, same length, same write time.</summary>
    private bool IsUnchanged(SourceFile file)
        => _fileSystem.FileExists(file.FullPath)
           && _fileSystem.GetFileLength(file.FullPath) == file.FileSize
           && _fileSystem.GetLastWriteTimeUtc(file.FullPath) == file.LastWriteTimeUtc;

    private static string ChangedSinceHashed(string path)
        => $"{path} changed or disappeared after it was hashed. Remove that source and add it again.";

    private async Task<ApplyUpdateResult> CopyAndSaveAsync(
        Instance instance,
        UpdatePlan plan,
        List<SourceFile> newContent,
        PendingChanges changes,
        IProgress<string>? log,
        IProgress<double>? percent,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var bytes = newContent.Sum(f => f.FileSize);
        var progress = ProgressStep.Over(Math.Max(newContent.Count, 1));
        var index = 0;
        foreach (var file in newContent)
        {
            ct.ThrowIfCancellationRequested();

            // Checked once before Apply, and again right before its own copy: the low
            // disk question and the copies before this one can each take minutes.
            // Nothing is saved on a mismatch, so the entry stays as it was. What was
            // copied so far stays in Storage unreferenced until the next storage
            // cleanup, the same as after a failed copy.
            if (!IsUnchanged(file))
            {
                return Fail(instance.InstanceName, ChangedSinceHashed(file.FullPath));
            }

            await _store.CopyToStoreAsync(file.FullPath, file.HashedFileName!, ct);
            status?.Report($"Added {file.FullPath} to storage");
            percent?.Report(progress.Report(ref index));
        }

        ct.ThrowIfCancellationRequested();
        var updated = Instance.Create(instance.InstanceName, plan.ResultingFiles, plan.Directories);
        updated.DetectedGame = changes.DetectedGame ?? instance.DetectedGame;
        updated.CustomLogoSource = instance.CustomLogoSource;

        // One database save with no steps to count. 100 tells the caller the
        // measurable part is over, and the status names what still runs.
        percent?.Report(100);
        status?.Report($"Saving {instance.InstanceName} ({updated.TotalFileCount} files)...");
        await _repository.SaveAsync(updated, ct);

        var result = new ApplyUpdateResult(
            true,
            null,
            plan.Files.Count(f => f.Status == PlannedStatus.Added),
            plan.Files.Count(f => f.Status == PlannedStatus.Replaced),
            plan.Files.Count(f => f.Status == PlannedStatus.Removed),
            bytes);
        log?.Report(
            $"Updated {instance.InstanceName}: {result.Added} added, {result.Replaced} replaced, " +
            $"{result.Removed} removed. {SizeFormatter.Format(bytes)} added to storage.");
        LogApplied(instance.InstanceName, result.Added, result.Replaced, result.Removed, bytes);
        return result;
    }

    private ApplyUpdateResult Fail(string instanceName, string error)
    {
        LogApplyFailed(instanceName, error);
        return new ApplyUpdateResult(false, error);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanned a source: {Files} files, {Directories} directories, {Issues} issues")]
    private partial void LogScanned(int files, int directories, int issues);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not hash {File}: {Error}")]
    private partial void LogHashFailed(string file, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Low disk space while updating '{InstanceName}': {FreeSpace} free, {SizeToCopy} to copy")]
    private partial void LogLowDisk(string instanceName, long freeSpace, long sizeToCopy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update of '{InstanceName}' refused: {Error}")]
    private partial void LogApplyFailed(string instanceName, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated instance '{InstanceName}': {Added} added, {Replaced} replaced, {Removed} removed, {BytesAdded} bytes added to storage")]
    private partial void LogApplied(string instanceName, int added, int replaced, int removed, long bytesAdded);
}
