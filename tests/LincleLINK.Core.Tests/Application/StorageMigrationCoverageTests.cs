using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Persistence;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Failure-isolation branches of <see cref="StorageMigrationService"/>: empty
/// instance dir, per-instance save failure, unmovable quarantine target, and an
/// undeletable legacy manifest.
/// </summary>
public sealed class StorageMigrationCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<LincleLinkDbContext> _factory;
    private readonly SqliteInstanceRepository _repository;

    public StorageMigrationCoverageTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        var services = new ServiceCollection();
        services.AddDbContextFactory<LincleLinkDbContext>(
            o => o.UseSqlite(LincleLinkPersistence.ConnectionStringFor(_paths.DataDirectory)));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();
        _repository = new SqliteInstanceRepository(_factory);
    }

    public void Dispose() => _temp.Dispose();

    private StorageMigrationService CreateService(IInstanceRepository? repository = null)
        => new(_paths, repository ?? _repository, _factory);

    private void WriteInstanceJson(string name, string json)
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        File.WriteAllText(Path.Combine(_paths.InstanceDirectory, name + ".json"), json);
    }

    private static string MinimalJson(string name)
        => $$"""{"Name":"{{name}}","TotalFileSize":0,"TotalFileCount":0,"TotalFileSizeString":"0 B"}""";

    [Fact]
    public async Task MigrateAsync_with_no_json_files_returns_empty_result()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        var service = CreateService();

        var result = await service.MigrateAsync(ct: TestContext.Current.CancellationToken);

        result.Migrated.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Quarantined.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_when_per_instance_save_throws()
    {
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("bulk failed"));
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ArgumentException("bad name"));
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var logs = new List<string>();
        var service = CreateService(repository);

        var result = await service.MigrateAsync(new SynchronousProgress<string>(logs.Add), ct: TestContext.Current.CancellationToken);

        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("bad name");
        logs.Should().Contain(m => m.Contains("Quarantined IIDX28"));
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "IIDX28.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_logs_when_quarantine_fails()
    {
        // Occupy instance-corrupt with a file so Directory.CreateDirectory throws.
        Directory.CreateDirectory(_paths.InstanceDirectory);
        File.WriteAllText(Path.Combine(_paths.InstanceDirectory, "instance-corrupt"), "occupied");
        WriteInstanceJson("bad", "{ this is not json");
        var logs = new List<string>();
        var service = CreateService();

        var result = await service.MigrateAsync(new SynchronousProgress<string>(logs.Add), ct: TestContext.Current.CancellationToken);

        result.Quarantined.Should().Be(1);
        logs.Should().Contain(m => m.Contains("Could not quarantine"));
    }

    [Fact]
    public async Task MigrateAsync_logs_when_legacy_delete_fails()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Read-only file deletion only throws on Windows.");
        }

        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        File.SetAttributes(Path.Combine(_paths.InstanceDirectory, "IIDX28.json"), FileAttributes.ReadOnly);
        var logs = new List<string>();
        var service = CreateService();

        try
        {
            var result = await service.MigrateAsync(new SynchronousProgress<string>(logs.Add), ct: TestContext.Current.CancellationToken);

            result.Migrated.Should().Be(1);
            logs.Should().Contain(m => m.Contains("Could not delete"));
        }
        finally
        {
            File.SetAttributes(Path.Combine(_paths.InstanceDirectory, "IIDX28.json"), FileAttributes.Normal);
        }
    }
}
