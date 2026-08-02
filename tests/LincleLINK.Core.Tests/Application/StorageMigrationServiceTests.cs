using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Persistence;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class StorageMigrationServiceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly IDbContextFactory<LincleLinkDbContext> _factory;
    private readonly SqliteInstanceRepository _repository;

    public StorageMigrationServiceTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        var services = new ServiceCollection();
        services.AddDbContextFactory<LincleLinkDbContext>(
            o => o.UseSqlite(LincleLinkPersistence.ConnectionStringFor(_paths.DataDirectory)));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();
        _repository = new SqliteInstanceRepository(_factory);
    }

    public void Dispose() => _temp.Dispose();

    private StorageMigrationService CreateService() => new(_paths, _repository, _factory);

    private void WriteInstanceJson(string name, string json)
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        File.WriteAllText(Path.Combine(_paths.InstanceDirectory, name + ".json"), json);
    }

    [Fact]
    public void NeedsMigration_requires_existing_json()
    {
        var service = CreateService();
        service.NeedsMigration().Should().BeFalse();

        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        service.NeedsMigration().Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_moves_manifests_into_sqlite_and_deletes_json()
    {
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        WriteInstanceJson("Dupe", """{"Name":"Dupe","TotalFileSize":0,"TotalFileCount":0,"TotalFileSizeString":"0 B"}""");
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Quarantined.Should().Be(0);
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();

        var loaded = await _repository.GetAsync("IIDX28");
        loaded.Should().NotBeNull();
        loaded!.FileList.Should().ContainSingle();
        loaded.FileList[0].HashedFileName.Should().Be("7AFE6AC1B80128D44BA5357D4349B21A.2dx");
        loaded.DirectoryList.Should().Equal(@"sound\25063");
    }

    [Fact]
    public async Task MigrateAsync_is_idempotent_across_partial_runs()
    {
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService();

        var first = await service.MigrateAsync();
        first.Migrated.Should().Be(1);

        // Simulate a crash leftover: the JSON reappears for an already-migrated name.
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var second = await service.MigrateAsync();

        second.Migrated.Should().Be(0);
        second.Skipped.Should().Be(1);
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_corrupt_json_and_continues()
    {
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        WriteInstanceJson("bad", "{ this is not json");
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(1);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("bad");

        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "bad.json")).Should().BeTrue();

        var loaded = await _repository.GetAsync("IIDX28");
        loaded.Should().NotBeNull();
        (await _repository.ExistsAsync("bad")).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_applies_migrations_and_creates_db_file()
    {
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService();

        await service.MigrateAsync();

        File.Exists(Path.Combine(_paths.DataDirectory, LincleLinkPersistence.DatabaseFileName)).Should().BeTrue();
    }
}
