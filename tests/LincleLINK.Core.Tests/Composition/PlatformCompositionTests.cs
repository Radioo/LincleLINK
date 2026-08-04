using FluentAssertions;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Composition;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Infrastructure.Linking;
using Xunit;

#pragma warning disable CA1416 // the factory deliberately constructs every platform implementation

namespace LincleLINK.Core.Tests.Composition;

/// <summary>
/// Every platform branch of the composition's OS factories, exercised on any host
/// OS by passing explicit OS flags (the DI lambdas call the same factories with
/// <c>OperatingSystem.IsX()</c> results, so this test is the single source of truth
/// for all branches).
/// </summary>
public sealed class PlatformCompositionTests
{
    [Fact]
    public void CreateDriveInfoProvider_windows_returns_DriveInfoProvider()
    {
        ServiceCollectionExtensions.CreateDriveInfoProvider(true, false, false)
            .Should().BeOfType<DriveInfoProvider>();
    }

    [Fact]
    public void CreateDriveInfoProvider_linux_returns_UnixStatFsDriveInfoProvider()
    {
        ServiceCollectionExtensions.CreateDriveInfoProvider(false, true, false)
            .Should().BeOfType<UnixStatFsDriveInfoProvider>();
    }

    [Fact]
    public void CreateDriveInfoProvider_macos_returns_MacDriveInfoProvider()
    {
        ServiceCollectionExtensions.CreateDriveInfoProvider(false, false, true)
            .Should().BeOfType<MacDriveInfoProvider>();
    }

    [Fact]
    public void CreateDriveInfoProvider_unknown_platform_throws()
    {
        var act = () => ServiceCollectionExtensions.CreateDriveInfoProvider(false, false, false);

        act.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows, Linux and macOS*");
    }

    [Fact]
    public void CreateHardLinker_windows_returns_Win32HardLinker()
    {
        ServiceCollectionExtensions.CreateHardLinker(true, false)
            .Should().BeOfType<Win32HardLinker>();
    }

    [Fact]
    public void CreateHardLinker_unix_returns_UnixHardLinker()
    {
        ServiceCollectionExtensions.CreateHardLinker(false, true)
            .Should().BeOfType<UnixHardLinker>();
    }

    [Fact]
    public void CreateHardLinker_unknown_platform_throws()
    {
        var act = () => ServiceCollectionExtensions.CreateHardLinker(false, false);

        act.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows, Linux and macOS*");
    }
}
