using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;

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
public sealed class InstanceService
{
    private const long LowDiskWiggleRoom = 100_000_000; // 100 MB, v2 parity

    private readonly IFileSystem _fileSystem;
    private readonly IFileHasher _hasher;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IInstanceRepository _repository;
    private readonly IDriveInfoProvider _driveInfo;
    private readonly IDialogService _dialogs;

    public InstanceService(
        IFileSystem fileSystem,
        IFileHasher hasher,
        IFileStore store,
        IHardLinker hardLinker,
        IInstanceRepository repository,
        IDriveInfoProvider driveInfo,
        IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _hasher = hasher;
        _store = store;
        _hardLinker = hardLinker;
        _repository = repository;
        _driveInfo = driveInfo;
        _dialogs = dialogs;
    }

    public async Task<AddInstanceResult> CreateInstanceAsync(
        AddInstanceRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var nameError = InstanceNameValidator.FirstError(request.InstanceName);
        if (nameError is not null)
        {
            return Fail(nameError);
        }

        if (!_fileSystem.DirectoryExists(request.DataPath))
        {
            return Fail("Data path does not exist or is not a directory.");
        }

        if (await _repository.ExistsAsync(request.InstanceName, ct))
        {
            return Fail("An instance with this name already exists.");
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
            return Fail("The data path contains no files.");
        }

        // Low-disk warning only in copy mode; free space measured on the data-path volume.
        if (request.Mode == CopyMoveMode.Copy)
        {
            long freeSpace = _driveInfo.GetAvailableFreeSpace(request.DataPath);
            if (enumerated.SizeToCopy + LowDiskWiggleRoom > freeSpace)
            {
                var proceed = await _dialogs.ConfirmAsync(
                    $"Current drive is low on disk space, do you want to continue? " +
                    $"Free space: {SizeFormatter.Format(freeSpace)}, " +
                    $"Size of files about to copy: {SizeFormatter.Format(enumerated.SizeToCopy)}",
                    "Low disk space!");
                if (!proceed)
                {
                    log?.Report("Operation cancelled.");
                    return new AddInstanceResult(false, null, 0, 0, 0, files.Count);
                }
            }
        }

        // Hash, dedup-copy and save off the UI thread; log/percent marshal back to it.
        return await Task.Run(() => RunPhasesAsync(request, files, log, percent, ct), ct);
    }

    private static AddInstanceResult Fail(string error) => new(false, error, 0, 0, 0, 0);

    /// <summary>
    /// Phase A + Phase B of add-instance: parallel hashing, then serialized dedup
    /// copy/move into the store and the manifest save. Runs on the thread pool
    /// (called via <see cref="Task.Run"/>); progress and log lines marshal to the
    /// caller's context through <paramref name="log"/> / <paramref name="percent"/>.
    /// </summary>
    private async Task<AddInstanceResult> RunPhasesAsync(
        AddInstanceRequest request,
        IReadOnlyList<string> files,
        IProgress<string>? log,
        IProgress<double>? percent,
        CancellationToken ct)
    {
        int filesAdded = 0;
        int alreadyExisted = 0;
        long bytesAdded = 0;
        var instanceFiles = new List<InstanceFile>(files.Count);
        var directories = new HashSet<string>(StringComparer.Ordinal);

        log?.Report($"Hashing {files.Count} files...");

        // Phase A: hash every file in parallel (bounded), capturing the length before any
        // mutation. Results are index-aligned so phase B stays in enumeration order.
        var hashResults = new HashResult[files.Count];
        int hashed = 0;
        double hashStep = files.Count == 0 ? 0 : 50d / files.Count;
        var maxDegree = Math.Clamp(request.MaxDegreeOfParallelism ?? Environment.ProcessorCount, 1, Environment.ProcessorCount);

        await Parallel.ForEachAsync(
            files.Select((path, index) => (path, index)),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree, CancellationToken = ct },
            async (item, token) =>
            {
                var hash = await _hasher.ComputeHashAsync(item.path, token);
                // Log after hashing completes so lines stream as work finishes, not all at once.
                log?.Report($"Hashed {item.path}");
                hashResults[item.index] = new HashResult(item.path, hash, _fileSystem.GetFileLength(item.path));
                percent?.Report(Interlocked.Increment(ref hashed) * hashStep);
            });

        // Phase B: write to the store in original order (dedup, copy / move-link-back, count).
        double writeStep = files.Count == 0 ? 0 : 50d / files.Count;
        int written = 0;

        foreach (var result in hashResults)
        {
            ct.ThrowIfCancellationRequested();

            var storeName = result.Hash + Path.GetExtension(result.Path);
            var fileLength = result.Length;

            var relativePath = Path.GetRelativePath(request.DataPath, Path.GetDirectoryName(result.Path) ?? request.DataPath);
            if (relativePath == ".")
            {
                relativePath = string.Empty;
            }

            directories.Add(relativePath);
            instanceFiles.Add(new InstanceFile(Path.GetFileName(result.Path), relativePath, fileLength, storeName));

            var isNew = !_store.Exists(storeName);

            if (request.Mode == CopyMoveMode.Move)
            {
                if (isNew)
                {
                    await _store.CopyToStoreAsync(result.Path, storeName, ct);
                }

                // Move: ensure a db copy exists (dedup skips the copy), then replace the
                // original with a hard link to the db file so the source path keeps
                // working while the data is deduplicated in db/.
                LinkOriginalToStore(result.Path, storeName, log);

                log?.Report(isNew
                    ? $"Moved {result.Path} into the db (linked back into place)"
                    : $"{result.Path} already exists in the db");
            }
            else if (isNew)
            {
                await _store.CopyToStoreAsync(result.Path, storeName, ct);
                log?.Report($"Added {result.Path} to the db");
            }
            else
            {
                log?.Report($"{result.Path} already exists in the db");
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
            $"Instance added. {alreadyExisted} files already exist. " +
            $"{SizeFormatter.Format(bytesAdded)} added to the db.");
        percent?.Report(100);

        return new AddInstanceResult(true, null, filesAdded, bytesAdded, alreadyExisted, files.Count);
    }

    private readonly record struct HashResult(string Path, string Hash, long Length);

    /// <summary>
    /// Replaces the original file at its source path with a hard link to its db copy.
    /// The data is already safely in <c>db/</c>, so a failed link is non-destructive;
    /// it is reported on the log and the file stays in the store.
    /// </summary>
    private void LinkOriginalToStore(string originalPath, string storeName, IProgress<string>? log)
    {
        var dbPath = _store.GetPath(storeName);
        _fileSystem.DeleteFile(originalPath);

        if (_hardLinker.TryCreateLink(dbPath, originalPath, out var error))
        {
            return;
        }

        log?.Report(
            $"File {Path.GetFileName(originalPath)} was added to the db but could not be " +
            $"hard-linked back into place ({error}); it is safe in the db.");
    }

    /// <summary>
    /// Deletes an instance manifest (files stay in <c>db/</c>) after a confirmation.
    /// </summary>
    public async Task<DeleteInstanceResult> DeleteInstanceAsync(string instanceName, CancellationToken ct = default)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            $"Delete {instanceName}? This will not delete the actual files.",
            "Delete instance");

        if (!confirmed)
        {
            return new DeleteInstanceResult(false, true);
        }

        var deleted = await _repository.DeleteAsync(instanceName, ct);
        return new DeleteInstanceResult(deleted, false);
    }
}
