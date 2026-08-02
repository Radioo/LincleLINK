using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class StorageMigrationViewModelTests
{
    private static StorageMigrationService CreateService(StorageMigrationResult result)
    {
        var service = Substitute.ForPartsOf<StorageMigrationService>(
            Substitute.For<IAppPaths>(),
            Substitute.For<IInstanceRepository>(),
            Substitute.For<IDbContextFactory<LincleLinkDbContext>>());
        service.MigrateAsync(
                Arg.Any<IProgress<string>>(),
                Arg.Any<IProgress<double>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        return service;
    }

    private static StorageMigrationService CreateFailingService(Exception failure)
    {
        var service = Substitute.ForPartsOf<StorageMigrationService>(
            Substitute.For<IAppPaths>(),
            Substitute.For<IInstanceRepository>(),
            Substitute.For<IDbContextFactory<LincleLinkDbContext>>());
        service.MigrateAsync(
                Arg.Any<IProgress<string>>(),
                Arg.Any<IProgress<double>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StorageMigrationResult>(failure));
        return service;
    }

    [Fact]
    public async Task RunAsync_reports_completion_and_closes()
    {
        var vm = new StorageMigrationViewModel(CreateService(new StorageMigrationResult(3, 1, 0, [])));
        var completed = false;
        var closed = false;
        vm.Completed += (_, _) => completed = true;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.RunAsync();

        completed.Should().BeTrue();
        closed.Should().BeTrue();
        vm.Status.Should().Be("Upgrade complete.");
        vm.LogLines.Should().Contain(line => line.Contains("Migrated 3"));
    }

    [Fact]
    public async Task RunAsync_reports_quarantined_manifests_in_status()
    {
        var vm = new StorageMigrationViewModel(CreateService(new StorageMigrationResult(1, 0, 1, ["bad: corrupt"])));

        await vm.RunAsync();

        vm.Status.Should().Contain("quarantined");
        vm.LogLines.Should().Contain(line => line.Contains("quarantined 1"));
    }

    [Fact]
    public async Task RunAsync_on_failure_still_closes_and_reports_error()
    {
        var vm = new StorageMigrationViewModel(CreateFailingService(new IOException("db locked")));
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.RunAsync();

        closed.Should().BeTrue();
        vm.Status.Should().Contain("Upgrade failed");
        vm.LogLines.Should().Contain(line => line.Contains("db locked"));
    }
}
