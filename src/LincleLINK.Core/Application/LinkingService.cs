using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record LinkResult(bool Cancelled, string? Error, int Linked, int Failed, IReadOnlyList<string> Errors);

public sealed record CopyHashedResult(bool Cancelled, string? Error, int Copied, int AlreadyExisted);

/// <summary>
/// Materializes <c>db/</c> data back to disk: hard-link an instance to a target
/// directory, or copy its hashed files flat (plan 06). Folder picks and
/// confirmations go through <see cref="IDialogService"/>. Per-file hard-link
/// failures are logged and the operation continues.
/// </summary>
public sealed class LinkingService
{
    private readonly IFileSystem _fileSystem;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IInstanceRepository _repository;
    private readonly IDialogService _dialogs;

    public LinkingService(
        IFileSystem fileSystem,
        IFileStore store,
        IHardLinker hardLinker,
        IInstanceRepository repository,
        IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _store = store;
        _hardLinker = hardLinker;
        _repository = repository;
        _dialogs = dialogs;
    }

    public async Task<LinkResult> LinkInstanceAsync(
        string instanceName,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var target = await _dialogs.PickFolderAsync("Select link target directory");
        if (target is null)
        {
            return new LinkResult(true, null, 0, 0, []);
        }

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, instanceName, ct);
        if (instance is null)
        {
            return new LinkResult(false, notFound, 0, 0, []);
        }

        var errors = new List<string>();
        int linked = 0;

        // 1. Create the directory structure (v2 order: dirs first).
        foreach (var dir in instance.DirectoryList)
        {
            ct.ThrowIfCancellationRequested();

            if (!PathNormalizer.IsSafeRelativePath(dir))
            {
                errors.Add($"Skipped unsafe directory path '{dir}'.");
                continue;
            }

            var targetDir = PathNormalizer.ToPlatformSeparators(Path.Combine(target, dir));
            _fileSystem.CreateDirectory(targetDir);
        }

        // 2. Duplicate detection.
        var dupes = instance.FileList.Count(f =>
            TryBuildTargetPath(target, f.RelativePath, f.FileName, out var p)
            && _fileSystem.FileExists(p));

        if (dupes > 0)
        {
            var proceed = await _dialogs.ConfirmAsync(
                $"{dupes} duplicate files already exist in the target directory. " +
                "Do you want to delete each one before linking new ones? " +
                "'No' cancels the operation entirely",
                "Duplicate files detected");

            if (!proceed)
            {
                return new LinkResult(true, null, 0, 0, errors);
            }

            foreach (var file in instance.FileList)
            {
                if (TryBuildTargetPath(target, file.RelativePath, file.FileName, out var existing)
                    && _fileSystem.FileExists(existing))
                {
                    _fileSystem.DeleteFile(existing);
                }
            }
        }

        // 3. Link each file; per-file failures log and continue.
        log?.Report("Linking...");
        var progress = ProgressStep.Over(instance.FileList.Count);
        int index = 0;

        foreach (var file in instance.FileList)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryBuildTargetPath(target, file.RelativePath, file.FileName, out var targetPath))
            {
                errors.Add($"{file.FileName}: unsafe path skipped.");
                continue;
            }

            if (_hardLinker.TryCreateLink(_store.GetPath(file.HashedFileName), targetPath, out var error))
            {
                linked++;
            }
            else
            {
                errors.Add($"{file.FileName}: {error}");
            }

            percent?.Report(progress.Report(ref index));
        }

        if (errors.Count > 0)
        {
            log?.Report($"Linking finished with {errors.Count} failure(s).");
        }
        else
        {
            log?.Report("Done!");
        }

        return new LinkResult(false, null, linked, errors.Count, errors);
    }

    public async Task<CopyHashedResult> CopyHashedFilesAsync(
        string instanceName,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var dest = await _dialogs.PickFolderAsync("Select destination");
        if (dest is null)
        {
            return new CopyHashedResult(true, null, 0, 0);
        }

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, instanceName, ct);
        if (instance is null)
        {
            return new CopyHashedResult(false, notFound, 0, 0);
        }

        int copied = 0;
        int alreadyExisted = 0;
        var progress = ProgressStep.Over(instance.FileList.Count);
        int index = 0;

        foreach (var file in instance.FileList)
        {
            ct.ThrowIfCancellationRequested();

            var destination = Path.Combine(dest, file.HashedFileName);
            if (_fileSystem.FileExists(destination))
            {
                alreadyExisted++;
                log?.Report($"{destination} already exists.");
            }
            else
            {
                await _store.CopyFromStoreAsync(file.HashedFileName, destination, ct);
                copied++;
            }

            percent?.Report(progress.Report(ref index));
        }

        return new CopyHashedResult(false, null, copied, alreadyExisted);
    }

    /// <summary>
    /// Builds a path under <paramref name="target"/>, rejecting any component that
    /// could escape it (rooted, '..', drive-letter segments). Mirrors the guard in
    /// <see cref="TorrentService.LinkToTorrentAsync"/> so manifest-derived paths
    /// cannot write outside the user-chosen directory.
    /// </summary>
    private static bool TryBuildTargetPath(string target, string relativePath, string fileName, out string path)
    {
        if (!PathNormalizer.IsSafeRelativePath(relativePath)
            || string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains('/')
            || fileName.Contains('\\'))
        {
            path = string.Empty;
            return false;
        }

        var combined = string.IsNullOrEmpty(relativePath)
            ? Path.Combine(target, fileName)
            : Path.Combine(target, relativePath, fileName);

        path = PathNormalizer.ToPlatformSeparators(combined);
        return true;
    }
}
