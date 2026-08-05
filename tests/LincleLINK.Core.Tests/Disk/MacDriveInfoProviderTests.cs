using FluentAssertions;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;
using Xunit.Sdk;

#pragma warning disable CA1416 // DriveInfo is available cross-platform; the class annotation is only a hint

namespace LincleLINK.Core.Tests.Disk;

public sealed class MacDriveInfoProviderTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Free_space_on_a_real_volume_is_positive()
    {
        // The provider is only registered on macOS; elsewhere the assertion on
        // real host free space must not run.
        if (!OperatingSystem.IsMacOS())
        {
            throw SkipException.ForSkip("MacDriveInfoProvider is only registered on macOS.");
        }

        var provider = new MacDriveInfoProvider();

        provider.GetAvailableFreeSpace(_temp.Root).Should().BeGreaterThan(0);
    }
}
