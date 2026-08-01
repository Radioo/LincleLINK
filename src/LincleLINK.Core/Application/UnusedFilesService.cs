using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;

namespace LincleLINK.Core.Application;

public sealed record UnusedFilesResult(bool Cancelled, int Found, int Deleted);

/// <summary>
/// Finds and deletes <c>db/</c> files referenced by no instance (plan 06 §6).
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
        CancellationToken ct = default)
    {
        var all = await _store.GetAllHashedFileNamesAsync(ct);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var instance in await _repository.GetAllAsync(ct))
        {
            foreach (var file in instance.FileList)
            {
                referenced.Add(file.HashedFileName);
            }
        }

        var unused = all.Where(name => !referenced.Contains(name)).ToList();

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

        foreach (var name in unused)
        {
            await _store.DeleteAsync(name, ct);
        }

        log?.Report($"{unused.Count} unused files deleted.");
        return new UnusedFilesResult(false, unused.Count, unused.Count);
    }
}
