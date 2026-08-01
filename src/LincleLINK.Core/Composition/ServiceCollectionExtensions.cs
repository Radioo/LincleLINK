using Microsoft.Extensions.DependencyInjection;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Hashing;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Linking;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Storage;
using LincleLINK.Core.Infrastructure.Torrents;

namespace LincleLINK.Core.Composition;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers LincleLINK.Core services and infrastructure adapters. Platform
    /// conditional implementations (hard linkers, drive providers) are selected via
    /// <see cref="OperatingSystem.IsWindows"/>.
    /// <see cref="IAppPaths"/> and <see cref="ISettingsStore"/> are registered by the
    /// app bootstrapper, which resolves the data directory first (plan 03/08).
    /// </summary>
    public static IServiceCollection AddLincleLINKCore(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IFileHasher, Md5FileHasher>();
        services.AddSingleton<IAppPathsFactory, AppPathsFactory>();

        services.AddSingleton<IDriveInfoProvider>(_ =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new DriveInfoProvider();
            }

            if (OperatingSystem.IsLinux())
            {
                return new UnixStatFsDriveInfoProvider();
            }

            throw new PlatformNotSupportedException("LincleLINK supports Windows and Linux only.");
        });

        services.AddSingleton<IHardLinker>(_ =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Win32HardLinker();
            }

            if (OperatingSystem.IsLinux())
            {
                return new UnixHardLinker();
            }

            throw new PlatformNotSupportedException("LincleLINK supports Windows and Linux only.");
        });

        services.AddSingleton<IFileStore, FileStore>();
        services.AddSingleton<IInstanceRepository, JsonInstanceRepository>();
        services.AddSingleton<ITorrentSource, MonoTorrentSource>();

        services.AddSingleton<FirstLaunchService>();
        services.AddSingleton<LegacyImporter>();
        services.AddSingleton<InstanceService>();
        services.AddSingleton<StatusService>();
        services.AddSingleton<LinkingService>();
        services.AddSingleton<UnusedFilesService>();
        services.AddSingleton<TorrentService>();

        return services;
    }
}
