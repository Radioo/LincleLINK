using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
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
    private readonly IInstanceRepository _repository;
    private readonly IDriveInfoProvider _driveInfo;
    private readonly IDialogService _dialogs;

    public InstanceService(
        IFileSystem fileSystem,
        IFileHasher hasher,
        IFileStore store,
        IInstanceRepository repository,
        IDriveInfoProvider driveInfo,
        IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _hasher = hasher;
        _store = store;
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
                var proceed = _dialogs.Confirm(
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

            var relativePath = Path.GetRelativePath(request.DataPath, Path.GetDirectoryName(file) ?? request.DataPath);
            if (relativePath == ".")
            {
                relativePath = string.Empty;
            }

            directories.Add(relativePath);
            instanceFiles.Add(new InstanceFile(Path.GetFileName(file), relativePath, _fileSystem.GetFileLength(file), storeName));

            if (_store.Exists(storeName))
            {
                alreadyExisted++;
            }
            else
            {
                if (request.Mode == CopyMoveMode.Copy)
                {
                    await _store.CopyToStoreAsync(file, storeName, ct);
                }
                else
                {
                    await _store.MoveToStoreAsync(file, storeName, ct);
                }

                filesAdded++;
                bytesAdded += _fileSystem.GetFileLength(file);
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
}
