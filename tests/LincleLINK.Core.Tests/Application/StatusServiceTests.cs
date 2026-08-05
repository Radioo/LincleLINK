using FluentAssertions;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class StatusServiceTests
{
    [Fact]
    public async Task Summary_computes_db_size_savings_and_free_space()
    {
        var store = Substitute.For<IFileStore>();
        store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(10L);

        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            new InstanceListEntry("a", 1, 40, "40 B"),
        ]);

        var driveInfo = Substitute.For<IDriveInfoProvider>();
        driveInfo.GetAvailableFreeSpace("C:\\data").Returns(500L);

        var paths = Substitute.For<IAppPaths>();
        paths.DataDirectory.Returns("C:\\data");

        var summary = await new StatusService(store, repository, driveInfo, paths).GetSummaryAsync(TestContext.Current.CancellationToken);

        summary.DbSize.Should().Be(10);
        summary.InstancesTotalSize.Should().Be(40);
        summary.Savings.Should().Be(30);
        summary.FreeSpace.Should().Be(500);
        summary.SavingsString.Should().Be("30 B");
    }

    [Fact]
    public async Task Summary_clamps_negative_savings_to_zero()
    {
        var store = Substitute.For<IFileStore>();
        store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(100L);

        var repository = Substitute.For<IInstanceRepository>();
        repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            new InstanceListEntry("a", 1, 40, "40 B"),
        ]);

        var driveInfo = Substitute.For<IDriveInfoProvider>();
        driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(500L);

        var paths = Substitute.For<IAppPaths>();
        paths.DataDirectory.Returns("C:\\data");

        var summary = await new StatusService(store, repository, driveInfo, paths).GetSummaryAsync(TestContext.Current.CancellationToken);

        // db/ (100) exceeds the instance total (40): orphaned files, savings clamped.
        summary.Savings.Should().Be(0);
        summary.SavingsString.Should().Be("0 B");
    }
}
