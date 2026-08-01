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
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            Instance.Create("a", [new InstanceFile("f.bin", "", 40, "A".PadRight(32, 'A') + ".bin")], []),
        ]);

        var driveInfo = Substitute.For<IDriveInfoProvider>();
        driveInfo.GetAvailableFreeSpace("C:\\data").Returns(500L);

        var paths = Substitute.For<IAppPaths>();
        paths.DataDirectory.Returns("C:\\data");

        var summary = await new StatusService(store, repository, driveInfo, paths).GetSummaryAsync();

        summary.DbSize.Should().Be(10);
        summary.InstancesTotalSize.Should().Be(40);
        summary.Savings.Should().Be(30);
        summary.FreeSpace.Should().Be(500);
        summary.SavingsString.Should().Be("30 B");
    }
}
