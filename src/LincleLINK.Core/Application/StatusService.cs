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
    public string SavingsString => SizeFormatter.Format(Savings);
    public string FreeSpaceString => SizeFormatter.Format(FreeSpace);
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
        var instances = await _repository.GetAllAsync(ct);

        long instancesTotal = 0;
        foreach (var instance in instances)
        {
            instancesTotal += instance.TotalFileSize;
        }

        var freeSpace = _driveInfo.GetAvailableFreeSpace(_paths.DataDirectory);

        return new StatusSummary(dbSize, instancesTotal, instancesTotal - dbSize, freeSpace);
    }
}
