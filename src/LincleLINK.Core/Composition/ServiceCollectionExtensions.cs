using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Games;
using LincleLINK.Core.Infrastructure.Hashing;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Linking;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Persistence;
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
        services.AddSingleton<IGameVersionDetector, GameVersionDetector>();

        services.AddSingleton<IDriveInfoProvider>(_ =>
            CreateDriveInfoProvider(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS()));

        services.AddSingleton<IHardLinker>(_ =>
            CreateHardLinker(OperatingSystem.IsWindows(), OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()));

        services.AddSingleton<IHardLinkPreflight, HardLinkPreflight>();

        services.AddSingleton<IFileStore, FileStore>();

        // SQLite instance metadata (plan 13): a singleton context factory keeps the
        // singleton repository on short-lived contexts per operation. The DB file
        // resolves from the app's IAppPaths (data directory).
        services.AddDbContextFactory<LincleLinkDbContext>((sp, builder) =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            builder.UseSqlite(LincleLinkPersistence.ConnectionStringFor(paths.DataDirectory));
        });

        services.AddSingleton<IInstanceRepository, SqliteInstanceRepository>();
        services.AddSingleton<StorageMigrationService>();
        services.AddSingleton<ITorrentSource, MonoTorrentSource>();

        services.AddSingleton<LegacyImporter>();
        services.AddSingleton<InstanceService>();
        services.AddSingleton<StatusService>();
        services.AddSingleton<LinkingService>();
        services.AddSingleton<UnusedFilesService>();
        services.AddSingleton<TorrentService>();

        return services;
    }

    /// <summary>
    /// Selects the platform drive provider from OS flags, extracted from the DI
    /// lambda so every platform branch is unit-testable on any host OS.
    /// </summary>
    internal static IDriveInfoProvider CreateDriveInfoProvider(bool isWindows, bool isLinux, bool isMacOS)
    {
#pragma warning disable CA1416 // selection is explicitly parameterized by the caller's OS flags
        if (isWindows)
        {
            return new DriveInfoProvider();
        }

        if (isLinux)
        {
            return new UnixStatFsDriveInfoProvider();
        }

        if (isMacOS)
        {
            return new MacDriveInfoProvider();
        }
#pragma warning restore CA1416

        throw new PlatformNotSupportedException("LincleLINK supports Windows, Linux and macOS only.");
    }

    /// <summary>Selects the platform hard linker from OS flags (see <see cref="CreateDriveInfoProvider"/>).</summary>
    internal static IHardLinker CreateHardLinker(bool isWindows, bool isUnix)
    {
#pragma warning disable CA1416 // selection is explicitly parameterized by the caller's OS flags
        if (isWindows)
        {
            return new Win32HardLinker();
        }

        if (isUnix)
        {
            return new UnixHardLinker();
        }
#pragma warning restore CA1416

        throw new PlatformNotSupportedException("LincleLINK supports Windows, Linux and macOS only.");
    }
}
