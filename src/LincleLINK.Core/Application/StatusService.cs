using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record StatusSummary(long DbSize, long InstancesTotalSize, long Savings, long FreeSpace)
{
    public string DbSizeString => SizeFormatter.Format(DbSize);
    public string LibrarySizeString => SizeFormatter.Format(Math.Max(0, InstancesTotalSize));
    public string SavingsString => SizeFormatter.Format(Savings);
    public string FreeSpaceString => SizeFormatter.Format(FreeSpace);

    /// <summary>
    /// Storage size as a share of the library's un-deduplicated total (0..1) -
    /// drives the sidebar storage bar (plan 15 D1). 0 while the library is empty.
    /// </summary>
    public double StorageShare => InstancesTotalSize > 0
        ? Math.Clamp((double)DbSize / InstancesTotalSize, 0, 1)
        : 0;
}

/// <summary>Computes the Other-tab status lines (v2 UpdateDBSize logic).</summary>
public sealed class StatusService
{
    private readonly IFileStore _store;
    private readonly IInstanceRepository _repository;
    private readonly IDriveInfoProvider _driveInfo;
    private readonly IAppPaths _paths;

    public StatusService(
        IFileStore store,
        IInstanceRepository repository,
        IDriveInfoProvider driveInfo,
        IAppPaths paths)
    {
        _store = store;
        _repository = repository;
        _driveInfo = driveInfo;
        _paths = paths;
    }

    public async Task<StatusSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var dbSize = await _store.GetTotalSizeAsync(ct);
        var instances = await _repository.GetSummariesAsync(ct);

        long instancesTotal = 0;
        foreach (var summary in instances)
        {
            instancesTotal += summary.TotalFileSize;
        }

        var freeSpace = _driveInfo.GetAvailableFreeSpace(_paths.DataDirectory);

        // Savings can go negative when db/ holds orphaned files no instance references;
        // SizeFormatter rejects negative sizes, so clamp at zero.
        var savings = Math.Max(0, instancesTotal - dbSize);

        return new StatusSummary(dbSize, instancesTotal, savings, freeSpace);
    }
}
