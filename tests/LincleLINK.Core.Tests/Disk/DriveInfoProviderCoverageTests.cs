using FluentAssertions;
using LincleLINK.Core.Infrastructure.Disk;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

#pragma warning disable CA1416 // the tests run on all platforms but only exercise the provider on Windows

namespace LincleLINK.Core.Tests.Disk;

/// <summary>
/// Windows <see cref="DriveInfoProvider"/> branches: relative-path drive fallback
/// and unresolvable-drive failure. Guarded to Windows because the provider uses
/// DriveInfo prefix matching that only exists there.
/// </summary>
public sealed class DriveInfoProviderCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("DriveInfoProvider is Windows-only.");
        }
    }

    [Fact]
    public void Relative_path_resolves_to_current_drive()
    {
        RequireWindows();

        var drive = DriveInfoProvider.ResolveDrive("relative\\path");

        drive.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Absolute_path_resolves_to_its_drive()
    {
        RequireWindows();

        var drive = DriveInfoProvider.ResolveDrive(_temp.Root);

        drive.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Unresolvable_path_throws()
    {
        RequireWindows();

        var act = () => DriveInfoProvider.ResolveDrive(@"\\unreachable-server\share\file.bin");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No drive found*");
    }

    [Fact]
    public void Null_fallback_root_throws()
    {
        RequireWindows();

        var act = () => DriveInfoProvider.ResolveDrive("relative\\path", () => null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Could not resolve the drive*");
    }
}
