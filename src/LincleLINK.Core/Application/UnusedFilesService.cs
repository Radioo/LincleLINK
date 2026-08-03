using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record UnusedFilesResult(bool Cancelled, int Found, int Deleted, long FoundBytes = 0);

/// <summary>
/// Finds and deletes <c>db/</c> files referenced by no instance (plan 06 §6).
/// The referenced set is a column-only projection (never the full instances) and
/// the whole scan runs off the caller's thread, so a large library does not stall
/// the UI; deletion is parallelized with bounded parallelism.
/// </summary>
public sealed class UnusedFilesService
{
    private readonly IFileStore _store;
    private readonly IInstanceRepository _repository;
    private readonly IDialogService _dialogs;

    public UnusedFilesService(IFileStore store, IInstanceRepository repository, IDialogService dialogs)
    {
        _store = store;
        _repository = repository;
        _dialogs = dialogs;
    }

    public async Task<UnusedFilesResult> CheckAndDeleteAsync(
        IProgress<string>? log = null,
        CancellationToken ct = default,
        int threadCount = 0,
        IProgress<string>? status = null)
    {
        // Clamped worker count, mirroring the settings store: the "Others"-tab
        // value bounds both add-instance hashing and this deletion scan. The
        // default resolves to the core count (Environment.ProcessorCount is not a
        // compile-time constant, so it cannot be the default parameter value).
        var maxThreads = Environment.ProcessorCount;
        threadCount = Math.Clamp(threadCount > 0 ? threadCount : maxThreads, 1, maxThreads);

        // Compute the unused set off the caller's (UI) thread: the db/ directory
        // scan plus building a HashSet of every referenced hash over ~1M rows must
        // never block the interface. Dialogs below run back on the caller context.
        var (unused, unusedBytes) = await Task.Run(async () =>
        {
            var all = await _store.GetAllHashedFileNamesAsync(ct);
            var referenced = (await _repository.GetAllHashedFileNamesAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            var orphans = all.Where(name => !referenced.Contains(name)).ToList();

            // Per-file stat calls; stays inside this Task.Run so a large orphan
            // set never blocks the caller's (UI) thread.
            long bytes = 0;
            foreach (var name in orphans)
            {
                ct.ThrowIfCancellationRequested();
                bytes += _store.GetSize(name);
            }

            return (orphans, bytes);
        }, ct);

        if (unused.Count == 0)
        {
            await _dialogs.InfoAsync(
                "Storage is clean — every file belongs to a library entry.",
                "Clean up storage");
            return new UnusedFilesResult(false, 0, 0);
        }

        var confirmed = await _dialogs.ConfirmAsync(
            $"{unused.Count} files in storage ({SizeFormatter.Format(unusedBytes)}) " +
            "don't belong to any library entry. Delete them?",
            "Clean up storage");
        if (!confirmed)
        {
            return new UnusedFilesResult(true, unused.Count, 0, unusedBytes);
        }

        var deleted = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = threadCount,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(unused, options, async (name, token) =>
        {
            await _store.DeleteAsync(name, token);
            // Use the increment's return value: reading the shared field afterwards
            // lets parallel workers report duplicate or out-of-order counts. The
            // running counter is transient status; only the summary goes to the log.
            var count = Interlocked.Increment(ref deleted);
            status?.Report($"Deleted {count} of {unused.Count} unneeded files...");
        });

        log?.Report($"Deleted {deleted} files from storage ({SizeFormatter.Format(unusedBytes)} freed).");
        return new UnusedFilesResult(false, unused.Count, deleted, unusedBytes);
    }
}
