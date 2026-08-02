using FluentAssertions;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Disk;

public sealed class DriveInfoProviderTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IDriveInfoProvider CreateProvider()
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
    }

    [Fact]
    public void Free_space_on_current_volume_is_positive()
    {
        PlatformGuard.EnsureSupportedOs();

        var provider = CreateProvider();
        provider.GetAvailableFreeSpace(_temp.Root).Should().BeGreaterThan(0);
    }
}
