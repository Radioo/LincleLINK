using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
using Microsoft.Extensions.Logging;

namespace LincleLINK.Core.Application;

public sealed record AddInstanceRequest(
    string InstanceName,
    string DataPath,
    CopyMoveMode Mode,
    int? MaxDegreeOfParallelism = null);

public sealed record AddInstanceResult(
    bool Success,
    string? Error,          // user-presentable failure message (null on cancel/success)
    int FilesAdded,
    long BytesAdded,
    int AlreadyExisted,
    int TotalFiles);

public sealed record DeleteInstanceResult(bool Deleted, bool Cancelled);

/// <summary>
/// Add-instance use case (plan 05): validation incl. low-disk warning, hashing,
/// dedup copy/move into <c>db/</c>, directory collection, and instance save.
/// All dialogs go through <see cref="IDialogService"/>; the service stays UI-free.
/// </summary>
public sealed partial class InstanceService
{
    private const long LowDiskWiggleRoom = 100_000_000; // 100 MB, v2 parity

    private readonly IFileSystem _fileSystem;
    private readonly IFileHasher _hasher;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IHardLinkPreflight _preflight;
    private readonly IInstanceRepository _repository;
    private readonly IDriveInfoProvider _driveInfo;
    private readonly IDialogService _dialogs;
    private readonly ILogger<InstanceService> _logger;

    public InstanceService(
        IFileSystem fileSystem,
        IFileHasher hasher,
        IFileStore store,
        IHardLinker hardLinker,
        IHardLinkPreflight preflight,
        IInstanceRepository repository,
        IDriveInfoProvider driveInfo,
        IDialogService dialogs,
        ILogger<InstanceService> logger)
    {
        _fileSystem = fileSystem;
        _hasher = hasher;
        _store = store;
        _hardLinker = hardLinker;
        _preflight = preflight;
        _repository = repository;
        _driveInfo = driveInfo;
        _dialogs = dialogs;
        _logger = logger;
    }

    public async Task<AddInstanceResult> CreateInstanceAsync(
        AddInstanceRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var nameError = InstanceNameValidator.FirstError(request.InstanceName);
        if (nameError is not null)
        {
            LogAddFailed(request.InstanceName, nameError);
            return Fail(nameError);
        }

        if (!_fileSystem.DirectoryExists(request.DataPath))
        {
            LogAddFailed(request.InstanceName, "The folder does not exist or is not a directory.");
            return Fail("The folder does not exist or is not a directory.");
        }

        if (await _repository.ExistsAsync(request.InstanceName, ct))
        {
            LogAddFailed(request.InstanceName, "A library entry with this name already exists.");
            return Fail("A library entry with this name already exists.");
        }

        // Reclaim-space (move) mode replaces originals with hard links into
        // storage, which only works when the folder and storage share a volume.
        // One clear pre-flight failure beats N per-file link errors (plan 14 D2).
        if (request.Mode == CopyMoveMode.Move)
        {
            var reason = await Task.Run(() => _preflight.CheckLinkTo(request.DataPath), ct);
            if (!string.IsNullOrEmpty(reason))
            {
                LogAddFailed(request.InstanceName, $"Can't reclaim space from this folder: {reason}");
                return Fail(
                    $"Can't reclaim space from this folder: {reason} " +
                    "You can still add it with \"Keep originals\".");
            }
        }

        // Enumerate off the UI thread: recursive enumeration of a large tree can
        // otherwise block the caller (which is the UI thread during add-instance).
        // Copy mode also precomputes the total size here, since per-file metadata
        // on a network origin is one round-trip per file that shouldn't run serially
        // on the UI thread.
        var enumerated = await Task.Run(() =>
        {
            var fileList = _fileSystem.EnumerateFiles(request.DataPath, recursive: true);

            long total = 0;
            if (request.Mode == CopyMoveMode.Copy)
            {
                foreach (var file in fileList)
                {
                    total += _fileSystem.GetFileLength(file);
                }
            }

            return (Files: fileList, SizeToCopy: total);
        }, ct);

        var files = enumerated.Files;
        if (files.Count == 0)
        {
            LogAddFailed(request.InstanceName, "The folder contains no files.");
            return Fail("The folder contains no files.");
        }

        // Low-disk warning only in copy mode; free space measured on the data-path volume.
        if (request.Mode == CopyMoveMode.Copy)
        {
            long freeSpace = _driveInfo.GetAvailableFreeSpace(request.DataPath);
            if (enumerated.SizeToCopy + LowDiskWiggleRoom > freeSpace)
            {
                LogLowDisk(request.InstanceName, freeSpace, enumerated.SizeToCopy);
                var proceed = await _dialogs.ConfirmAsync(
                    $"This drive is low on disk space, do you want to continue? " +
                    $"Free space: {SizeFormatter.Format(freeSpace)}, " +
                    $"size of files about to be copied into storage: {SizeFormatter.Format(enumerated.SizeToCopy)}",
                    "Low disk space");
                if (!proceed)
                {
                    LogAddCancelled(request.InstanceName);
                    log?.Report("Operation cancelled.");
                    return new AddInstanceResult(false, null, 0, 0, 0, files.Count);
                }
            }
        }

        // Hash, dedup-copy and save off the UI thread; log/percent marshal back to it.
        return await Task.Run(() => HashAndStoreAsync(request, files, log, percent, status, ct), ct);
    }

    private static AddInstanceResult Fail(string error) => new(false, error, 0, 0, 0, 0);

    /// <summary>
    /// Converts a full path under the data root to its relative form for the
    /// manifest, mapping the root itself to an empty string (v2 behavior).
    /// </summary>
    private static string RelativePathFrom(string dataPath, string path)
    {
        var relativePath = Path.GetRelativePath(dataPath, path);
        return relativePath == "." ? string.Empty : relativePath;
    }

    /// <summary>
    /// Phase A + Phase B of add-instance: parallel hashing, then serialized dedup
    /// copy/move into the store and the manifest save. Runs on the thread pool
    /// (called via <see cref="Task.Run"/>); progress and log lines marshal to the
    /// caller's context through <paramref name="log"/> / <paramref name="percent"/>.
    /// </summary>
    private async Task<AddInstanceResult> HashAndStoreAsync(
        AddInstanceRequest request,
        IReadOnlyList<string> files,
        IProgress<string>? log,
        IProgress<double>? percent,
        IProgress<string>? status,
        CancellationToken ct)
    {
        int filesAdded = 0;
        int alreadyExisted = 0;
        long bytesAdded = 0;
        var instanceFiles = new List<InstanceFile>(files.Count);

        // V2 parity: collect every directory recursively (including empty ones) so
        // empty folders survive add-instance -> link. File-only derivation below
        // would drop directories that contain no files.
        var directories = new HashSet<string>(
            _fileSystem.EnumerateDirectories(request.DataPath, recursive: true)
                .Select(path => RelativePathFrom(request.DataPath, path)),
            StringComparer.Ordinal);

        log?.Report($"Hashing {files.Count} files...");
        LogHashingStart(files.Count, request.InstanceName);

        // Phase A: hash every file in parallel (bounded), capturing the length before any
        // mutation. Results are index-aligned so phase B stays in enumeration order.
        var hashResults = new HashResult[files.Count];
        int hashed = 0;
        double hashStep = 50d / files.Count;
        var maxDegree = Math.Clamp(request.MaxDegreeOfParallelism ?? Environment.ProcessorCount, 1, Environment.ProcessorCount);

        await Parallel.ForEachAsync(
            files.Select((path, index) => (path, index)),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct },
            async (item, token) =>
            {
                var hash = await _hasher.ComputeHashAsync(item.path, token);
                // Report after hashing completes so the status line streams as work
                // finishes; per-file lines go to the transient status channel, not
                // the log (plan 14 D5).
                status?.Report($"Hashed {item.path}");
                LogHashed(item.path);
                hashResults[item.index] = new HashResult(item.path, hash, _fileSystem.GetFileLength(item.path));
                percent?.Report(Interlocked.Increment(ref hashed) * hashStep);
            });

        // Phase B: write to the store in original order (dedup, copy / move-link-back, count).
        double writeStep = 50d / files.Count;
        int written = 0;

        foreach (var result in hashResults)
        {
            ct.ThrowIfCancellationRequested();

            var storeName = result.Hash + Path.GetExtension(result.Path);
            var fileLength = result.Length;

            var relativePath = RelativePathFrom(request.DataPath, Path.GetDirectoryName(result.Path) ?? request.DataPath);
            directories.Add(relativePath);
            instanceFiles.Add(new InstanceFile(Path.GetFileName(result.Path), relativePath, fileLength, storeName));

            var isNew = !_store.Exists(storeName);
            LogFileStored(result.Path, storeName, isNew);

            if (request.Mode == CopyMoveMode.Move)
            {
                if (isNew)
                {
                    await _store.CopyToStoreAsync(result.Path, storeName, ct);
                }

                // Reclaim space: ensure a storage copy exists (dedup skips the copy),
                // then replace the original with a hard link to it so the source path
                // keeps working while the data is deduplicated in db/.
                LinkOriginalToStore(result.Path, storeName, log);

                status?.Report(isNew
                    ? $"Reclaimed {result.Path} (now a link into storage)"
                    : $"{result.Path} was already in storage (now a link)");
            }
            else if (isNew)
            {
                await _store.CopyToStoreAsync(result.Path, storeName, ct);
                status?.Report($"Added {result.Path} to storage");
            }
            else
            {
                status?.Report($"{result.Path} is already in storage");
            }

            if (isNew)
            {
                filesAdded++;
                bytesAdded += fileLength;
            }
            else
            {
                alreadyExisted++;
            }

            percent?.Report(50 + (++written * writeStep));
        }

        var instance = Instance.Create(request.InstanceName, instanceFiles, directories);
        await _repository.SaveAsync(instance, ct);

        log?.Report(
            $"{LogMessages.EntryAdded} {alreadyExisted} files were already in storage. " +
            $"{SizeFormatter.Format(bytesAdded)} added to storage.");
        percent?.Report(100);

        LogAddCompleted(request.InstanceName, filesAdded, bytesAdded, alreadyExisted);
        return new AddInstanceResult(true, null, filesAdded, bytesAdded, alreadyExisted, files.Count);
    }

    private readonly record struct HashResult(string Path, string Hash, long Length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hashing {Count} files for instance '{InstanceName}'")]
    private partial void LogHashingStart(int count, string instanceName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hashed {File}")]
    private partial void LogHashed(string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored {File} as {StoreName} (new: {IsNew})")]
    private partial void LogFileStored(string file, string storeName, bool isNew);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not hard-link {File} back into place: {Error}")]
    private partial void LogLinkBackFailed(string file, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Add instance '{InstanceName}' completed: {FilesAdded} files, {BytesAdded} bytes, {AlreadyExisted} already existed")]
    private partial void LogAddCompleted(string instanceName, int filesAdded, long bytesAdded, int alreadyExisted);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted instance '{InstanceName}'")]
    private partial void LogInstanceDeleted(string instanceName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Low disk space while adding '{InstanceName}': {FreeSpace} free, {SizeToCopy} to copy")]
    private partial void LogLowDisk(string instanceName, long freeSpace, long sizeToCopy);

    [LoggerMessage(Level = LogLevel.Information, Message = "Add instance '{InstanceName}' failed: {Error}")]
    private partial void LogAddFailed(string instanceName, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Add instance '{InstanceName}' cancelled")]
    private partial void LogAddCancelled(string instanceName);

    /// <summary>
    /// Replaces the original file at its source path with a hard link to its
    /// storage copy. Link-then-replace order (plan 14 D3): the link is created at
    /// a temp name first and only then swapped over the original, so a failed
    /// link leaves the user's folder untouched.
    /// </summary>
    private void LinkOriginalToStore(string originalPath, string storeName, IProgress<string>? log)
    {
        var dbPath = _store.GetPath(storeName);
        var tempLink = $"{originalPath}.{Guid.NewGuid():N}.lincletmp";

        if (!_hardLinker.TryCreateLink(dbPath, tempLink, out var error))
        {
            LogLinkBackFailed(originalPath, error ?? "unknown error");
            log?.Report(
                $"File {Path.GetFileName(originalPath)} is in storage, but its original could not be " +
                $"replaced with a link ({error}). The original file was left unchanged.");
            return;
        }

        try
        {
            _fileSystem.MoveFile(tempLink, originalPath, overwrite: true);
        }
        catch
        {
            _fileSystem.DeleteFile(tempLink);
            throw;
        }
    }

    /// <summary>
    /// Deletes an instance manifest (files stay in <c>db/</c>) after a confirmation.
    /// </summary>
    public async Task<DeleteInstanceResult> DeleteInstanceAsync(string instanceName, CancellationToken ct = default)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            $"Remove {instanceName} from the library? Its files stay in storage " +
            "and any deployed folders are untouched.",
            "Remove from library");

        if (!confirmed)
        {
            return new DeleteInstanceResult(false, true);
        }

        var deleted = await _repository.DeleteAsync(instanceName, ct);
        if (deleted)
        {
            LogInstanceDeleted(instanceName);
        }

        return new DeleteInstanceResult(deleted, false);
    }
}
