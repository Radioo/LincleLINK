using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Infrastructure.Instances;

/// <summary>
/// Runs every call of another repository on the thread pool. The UI never freezes
/// (CLAUDE.md): Microsoft.Data.Sqlite has no real async I/O, so EF Core's
/// <c>...Async</c> methods do their work on the thread that calls them, and
/// <c>await repository.GetAsync(...)</c> from a view model would block the window
/// for as long as the query takes. An entry can hold 150k file rows. Wrapping the
/// repository once covers every caller, present and future, instead of relying on
/// each of them to remember a <see cref="Task.Run(Action)"/>.
/// </summary>
public sealed class BackgroundInstanceRepository : IInstanceRepository
{
    private readonly IInstanceRepository _inner;

    public BackgroundInstanceRepository(IInstanceRepository inner)
    {
        _inner = inner;
    }

    public Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default)
        => Task.Run(() => _inner.GetNamesAsync(ct), ct);

    public Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default)
        => Task.Run(() => _inner.GetAllAsync(ct), ct);

    public Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default)
        => Task.Run(() => _inner.GetAllHashedFileNamesAsync(ct), ct);

    public Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default)
        => Task.Run(() => _inner.GetSummariesAsync(ct), ct);

    public Task<Instance?> GetAsync(string name, CancellationToken ct = default)
        => Task.Run(() => _inner.GetAsync(name, ct), ct);

    public Task<bool> ExistsAsync(string name, CancellationToken ct = default)
        => Task.Run(() => _inner.ExistsAsync(name, ct), ct);

    public Task<long> GetUniqueSizeAsync(string name, CancellationToken ct = default)
        => Task.Run(() => _inner.GetUniqueSizeAsync(name, ct), ct);

    public Task SaveAsync(Instance instance, CancellationToken ct = default)
        => Task.Run(() => _inner.SaveAsync(instance, ct), ct);

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
        => Task.Run(() => _inner.DeleteAsync(name, ct), ct);

    public Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default)
        => Task.Run(() => _inner.BulkInsertAsync(instances, ct), ct);

    public Task SetCustomLogoAsync(string name, string? logoSource, CancellationToken ct = default)
        => Task.Run(() => _inner.SetCustomLogoAsync(name, logoSource, ct), ct);
}
