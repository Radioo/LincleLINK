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

public sealed record AddInstanceRequest(string InstanceName, string DataPath, CopyMoveMode Mode);

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

        var files = _fileSystem.EnumerateFiles(request.DataPath, recursive: true);
        if (files.Count == 0)
        {
            return Fail("The data path contains no files.");
        }

        // Low-disk warning only in copy mode; free space measured on the data-path volume.
        if (request.Mode == CopyMoveMode.Copy)
        {
            long sizeToCopy = 0;
            foreach (var file in files)
            {
                sizeToCopy += _fileSystem.GetFileLength(file);
            }

            long freeSpace = _driveInfo.GetAvailableFreeSpace(request.DataPath);
            if (sizeToCopy + LowDiskWiggleRoom > freeSpace)
            {
                var proceed = await _dialogs.ConfirmAsync(
                    $"Current drive is low on disk space, do you want to continue? " +
                    $"Free space: {SizeFormatter.Format(freeSpace)}, " +
                    $"Size of files about to copy: {SizeFormatter.Format(sizeToCopy)}",
                    "Low disk space!");
                if (!proceed)
                {
                    log?.Report("Operation cancelled.");
                    return new AddInstanceResult(false, null, 0, 0, 0, files.Count);
                }
            }
        }

        int filesAdded = 0;
        int alreadyExisted = 0;
        long bytesAdded = 0;
        var instanceFiles = new List<InstanceFile>(files.Count);
        var directories = new HashSet<string>(StringComparer.Ordinal);

        log?.Report("Hashing...");
        double step = files.Count == 0 ? 0 : 100d / files.Count;
        int index = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            log?.Report($"Hashing {file}");
            var hash = await _hasher.ComputeHashAsync(file, ct);
            var storeName = hash + Path.GetExtension(file);
            var fileLength = _fileSystem.GetFileLength(file);

            var relativePath = Path.GetRelativePath(request.DataPath, Path.GetDirectoryName(file) ?? request.DataPath);
            if (relativePath == ".")
            {
                relativePath = string.Empty;
            }

            directories.Add(relativePath);
            instanceFiles.Add(new InstanceFile(Path.GetFileName(file), relativePath, fileLength, storeName));

            var isNew = !_store.Exists(storeName);

            if (request.Mode == CopyMoveMode.Move)
            {
                if (isNew)
                {
                    await _store.CopyToStoreAsync(file, storeName, ct);
                }

                // Move: ensure a db copy exists (dedup skips the copy), then replace the
                // original with a hard link to the db file so the source path keeps
                // working while the data is deduplicated in db/.
                LinkOriginalToStore(file, storeName, log);
            }
            else if (isNew)
            {
                await _store.CopyToStoreAsync(file, storeName, ct);
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

            percent?.Report(++index * step);
        }

        var instance = Instance.Create(request.InstanceName, instanceFiles, directories);
        await _repository.SaveAsync(instance, ct);

        log?.Report(
            $"Instance added. {alreadyExisted} files already exist. " +
            $"{SizeFormatter.Format(bytesAdded)} added to the db.");
        percent?.Report(100);

        return new AddInstanceResult(true, null, filesAdded, bytesAdded, alreadyExisted, files.Count);
    }

    private static AddInstanceResult Fail(string error) => new(false, error, 0, 0, 0, 0);

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
