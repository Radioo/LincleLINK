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

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DbDirectory);
        Directory.CreateDirectory(InstanceDirectory);
    }
}
