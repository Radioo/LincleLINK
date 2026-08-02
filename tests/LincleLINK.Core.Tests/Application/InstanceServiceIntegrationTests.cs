using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Hashing;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Linking;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Storage;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>Real-filesystem checks for the add-instance flow (no mocked IO).</summary>
public sealed class InstanceServiceIntegrationTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IHardLinker CreateLinker()
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
    }

    [Fact]
    public async Task Move_mode_copies_to_db_and_hard_links_original_back()
    {
        PlatformGuard.EnsureSupportedOs();

        var linker = CreateLinker();
        var dataPath = Path.Combine(_temp.Root, "source");
        Directory.CreateDirectory(dataPath);
        var sourceFile = Path.Combine(dataPath, "a.bin");
        await File.WriteAllBytesAsync(sourceFile, [1, 2, 3, 4]);

        var paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        var service = new InstanceService(
            new FileSystem(),
            new Md5FileHasher(),
            new FileStore(paths),
            linker,
            new JsonInstanceRepository(paths),
            Substitute.For<IDriveInfoProvider>(),
            Substitute.For<IDialogService>());

        var result = await service.CreateInstanceAsync(new AddInstanceRequest("inst", dataPath, CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        result.FilesAdded.Should().Be(1);
        result.BytesAdded.Should().Be(4);

        var storeName = await new Md5FileHasher().ComputeHashAsync(sourceFile);
        storeName = storeName + Path.GetExtension(sourceFile);

        // The db has the hashed copy AND the original path still works (hard link back).
        new FileStore(paths).Exists(storeName).Should().BeTrue();
        File.Exists(sourceFile).Should().BeTrue();
        File.ReadAllBytes(sourceFile).Should().Equal(1, 2, 3, 4);

        // Instance manifest saved and readable.
        var loaded = await new JsonInstanceRepository(paths).GetAsync("inst");
        loaded.Should().NotBeNull();
        loaded!.FileList.Should().ContainSingle(f => f.FileName == "a.bin");
    }

    [Fact]
    public async Task Empty_directories_are_recorded_in_the_manifest()
    {
        PlatformGuard.EnsureSupportedOs();

        var linker = CreateLinker();
        var dataPath = Path.Combine(_temp.Root, "source");
        var emptyDir = Path.Combine(dataPath, "empty");
        Directory.CreateDirectory(emptyDir);
        var nested = Path.Combine(dataPath, "sub", "nested");
        Directory.CreateDirectory(nested);
        var sourceFile = Path.Combine(dataPath, "a.bin");
        await File.WriteAllBytesAsync(sourceFile, [1, 2, 3, 4]);

        var paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        var driveInfo = Substitute.For<IDriveInfoProvider>();
        driveInfo.GetAvailableFreeSpace(dataPath).Returns(1_000_000_000_000L);
        var service = new InstanceService(
            new FileSystem(),
            new Md5FileHasher(),
            new FileStore(paths),
            linker,
            new JsonInstanceRepository(paths),
            driveInfo,
            Substitute.For<IDialogService>());

        var result = await service.CreateInstanceAsync(new AddInstanceRequest("inst", dataPath, CopyMoveMode.Copy));

        result.Success.Should().BeTrue();

        var loaded = await new JsonInstanceRepository(paths).GetAsync("inst");
        loaded.Should().NotBeNull();
        loaded!.DirectoryList.Should().Contain(Path.Combine("empty"));
        loaded.DirectoryList.Should().Contain(Path.Combine("sub", "nested"));
    }
}
