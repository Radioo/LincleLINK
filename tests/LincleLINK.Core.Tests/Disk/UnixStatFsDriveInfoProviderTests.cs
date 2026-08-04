using FluentAssertions;
using LincleLINK.Core.Infrastructure.Disk;
using Xunit;

#pragma warning disable CA1416 // the free-space math is platform-independent and tested everywhere

namespace LincleLINK.Core.Tests.Disk;

/// <summary>
/// <see cref="UnixStatFsDriveInfoProvider"/> free-space math, exercised through the
/// injected statvfs seam so no libc is required on the test host.
/// </summary>
public sealed class UnixStatFsDriveInfoProviderTests
{
    [Fact]
    public void Free_space_is_bavail_times_frsize()
    {
        var provider = new UnixStatFsDriveInfoProvider(_ => new UnixStatFsDriveInfoProvider.StatVfs
        {
            f_bavail = 1000,
            f_frsize = 4096,
        });

        provider.GetAvailableFreeSpace("/mnt/game").Should().Be(4_096_000);
    }

    [Fact]
    public void Overflowing_capacity_throws()
    {
        var provider = new UnixStatFsDriveInfoProvider(_ => new UnixStatFsDriveInfoProvider.StatVfs
        {
            f_bavail = ulong.MaxValue,
            f_frsize = 2,
        });

        var act = () => provider.GetAvailableFreeSpace("/mnt/game");

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Statvfs_failure_is_reported()
    {
        var provider = new UnixStatFsDriveInfoProvider(_ => throw new InvalidOperationException("statvfs failed for '/mnt/game'."));

        var act = () => provider.GetAvailableFreeSpace("/mnt/game");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*statvfs failed*");
    }
}
