using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record LinkResult(
    bool Cancelled,
    string? Error,
    int Linked,
    int Failed,
    int SkippedExisting,
    IReadOnlyList<string> Errors);

public sealed record CopyHashedResult(bool Cancelled, string? Error, int Copied, int AlreadyExisted);

/// <summary>
/// Materializes <c>db/</c> data back to disk: deploy an instance to a target
/// directory via hard links, or export its hashed files flat (plan 06, plan 14).
/// Folder picks and confirmations go through <see cref="IDialogService"/>.
/// Per-file hard-link failures are collected, summarized on the log (capped), and
/// the operation continues.
/// </summary>
public sealed class LinkingService
{
    /// <summary>Per-file error lines logged before collapsing into an "…and N more." line.</summary>
    private const int MaxLoggedErrors = 20;

    private readonly IFileSystem _fileSystem;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IHardLinkPreflight _preflight;
    private readonly IInstanceRepository _repository;
    private readonly IDialogService _dialogs;

    public LinkingService(
        IFileSystem fileSystem,
        IFileStore store,
        IHardLinker hardLinker,
        IHardLinkPreflight preflight,
        IInstanceRepository repository,
        IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _store = store;
        _hardLinker = hardLinker;
        _preflight = preflight;
        _repository = repository;
        _dialogs = dialogs;
    }

    public async Task<LinkResult> LinkInstanceAsync(
        string instanceName,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var target = await _dialogs.PickFolderAsync($"Deploy {instanceName} - select a target folder");
        if (target is null)
        {
            return new LinkResult(true, null, 0, 0, 0, []);
        }

        // One clear cross-volume failure up front instead of one per file (plan 14 D2).
        var preflightError = await Task.Run(() => _preflight.CheckLinkTo(target), ct);
        if (!string.IsNullOrEmpty(preflightError))
        {
            await _dialogs.ErrorAsync($"Can't deploy to this folder: {preflightError}", "Deploy to folder");
            return new LinkResult(true, null, 0, 0, 0, []);
        }

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, instanceName, ct);
        if (instance is null)
        {
            return new LinkResult(false, notFound, 0, 0, 0, []);
        }

        var errors = new List<string>();
        int linked = 0;
        int skippedExisting = 0;

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

        // 2. Conflict detection: Replace / Skip existing / Cancel (plan 14 §3).
        var skipExisting = false;
        var dupes = instance.FileList.Count(f =>
            TryBuildTargetPath(target, f.RelativePath, f.FileName, out var p)
            && _fileSystem.FileExists(p));

        if (dupes > 0)
        {
            var choice = await _dialogs.AskConflictAsync(
                $"{dupes} of this entry's files already exist in the target folder. " +
                "Replace them with links into storage, skip them, or cancel?",
                "Files already exist");

            if (choice == ConflictChoice.Cancel)
            {
                return new LinkResult(true, null, 0, 0, 0, errors);
            }

            skipExisting = choice == ConflictChoice.Skip;
            if (choice == ConflictChoice.Replace)
            {
                foreach (var file in instance.FileList)
                {
                    if (TryBuildTargetPath(target, file.RelativePath, file.FileName, out var existing)
                        && _fileSystem.FileExists(existing))
                    {
                        _fileSystem.DeleteFile(existing);
                    }
                }
            }
        }

        // 3. Link each file; per-file failures log and continue.
        log?.Report($"Deploying {instanceName}...");
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

            if (skipExisting && _fileSystem.FileExists(targetPath))
            {
                skippedExisting++;
            }
            else if (_hardLinker.TryCreateLink(_store.GetPath(file.HashedFileName), targetPath, out var error))
            {
                linked++;
            }
            else
            {
                errors.Add($"{file.FileName}: {error}");
            }

            percent?.Report(progress.Report(ref index));
        }

        ReportSummary(log, linked, skippedExisting, errors);
        return new LinkResult(false, null, linked, errors.Count, skippedExisting, errors);
    }

    /// <summary>
    /// Deploy summary (plan 14 D4): one outcome line, then the per-file error
    /// details that were previously collected and discarded, capped at
    /// <see cref="MaxLoggedErrors"/> lines.
    /// </summary>
    private static void ReportSummary(IProgress<string>? log, int linked, int skippedExisting, List<string> errors)
    {
        var skippedNote = skippedExisting > 0 ? $" {skippedExisting} existing files were skipped." : string.Empty;

        if (errors.Count == 0)
        {
            log?.Report($"Deployed {linked} files.{skippedNote}");
            return;
        }

        log?.Report($"Deployed {linked} files; {errors.Count} failed:{skippedNote}");
        foreach (var error in errors.Take(MaxLoggedErrors))
        {
            log?.Report($"  {error}");
        }

        if (errors.Count > MaxLoggedErrors)
        {
            log?.Report($"  …and {errors.Count - MaxLoggedErrors} more.");
        }
    }

    public async Task<CopyHashedResult> CopyHashedFilesAsync(
        string instanceName,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var dest = await _dialogs.PickFolderAsync("Export storage files - select a destination folder");
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
                status?.Report($"{destination} already exists.");
            }
            else
            {
                await _store.CopyFromStoreAsync(file.HashedFileName, destination, ct);
                copied++;
                status?.Report($"Exported {file.HashedFileName}");
            }

            percent?.Report(progress.Report(ref index));
        }

        log?.Report(alreadyExisted > 0
            ? $"Exported {copied} files. {alreadyExisted} already existed and were skipped."
            : $"Exported {copied} files.");
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
