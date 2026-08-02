using LincleLINK.Core.Abstractions.Paths;

namespace LincleLINK.Core.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    public string DataDirectory { get; }
    public string DbDirectory { get; }
    public string InstanceDirectory { get; }

    public AppPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        DbDirectory = Path.Combine(dataDirectory, "db");
        InstanceDirectory = Path.Combine(dataDirectory, "instance");
    }

    /// <summary>
    /// Creates the data-root layout at startup. Only <c>db/</c> is created eagerly
    /// (the dedup store); since plan 13 moved instance manifests to SQLite, the
    /// <c>instance/</c> folder is created lazily by the legacy JSON migration path
    /// (<c>StorageMigrationService</c> / <c>JsonInstanceRepository</c>), so fresh
    /// installs no longer produce an empty directory.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DbDirectory);
    }
}
