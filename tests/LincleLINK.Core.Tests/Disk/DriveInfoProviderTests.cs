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

    private static IDriveInfoProvider? CreateProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DriveInfoProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return new UnixStatFsDriveInfoProvider();
        }

        return null;
    }

    [Fact]
    public void Free_space_on_current_volume_is_positive()
    {
        var provider = CreateProvider();
        if (provider is null)
        {
            return;
        }

        provider.GetAvailableFreeSpace(_temp.Root).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Total_size_on_current_volume_is_positive()
    {
        var provider = CreateProvider();
        if (provider is null)
        {
            return;
        }

        provider.GetTotalSize(_temp.Root).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Free_space_is_less_than_total_size()
    {
        var provider = CreateProvider();
        if (provider is null)
        {
            return;
        }

        provider.GetAvailableFreeSpace(_temp.Root).Should().BeLessThan(provider.GetTotalSize(_temp.Root));
    }
}
