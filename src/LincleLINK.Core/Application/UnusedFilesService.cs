using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;

namespace LincleLINK.Core.Application;

public sealed record UnusedFilesResult(bool Cancelled, int Found, int Deleted);

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
        int threadCount = 0)
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
        var unused = await Task.Run(async () =>
        {
            var all = await _store.GetAllHashedFileNamesAsync(ct);
            var referenced = (await _repository.GetAllHashedFileNamesAsync(ct))
                .ToHashSet(StringComparer.Ordinal);

            return all.Where(name => !referenced.Contains(name)).ToList();
        }, ct);

        if (unused.Count == 0)
        {
            await _dialogs.InfoAsync("No unused files found.", "No unused files");
            return new UnusedFilesResult(false, 0, 0);
        }

        var confirmed = await _dialogs.ConfirmAsync($"{unused.Count} unused files found. Delete?", "Unused files found");
        if (!confirmed)
        {
            return new UnusedFilesResult(true, unused.Count, 0);
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
            // lets parallel workers log duplicate or out-of-order counts.
            var count = Interlocked.Increment(ref deleted);
            log?.Report($"{count} unused files deleted.");
        });

        return new UnusedFilesResult(false, unused.Count, deleted);
    }
}
