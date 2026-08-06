using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

public sealed class SqliteInstanceRepositoryTests : InstanceRepositoryContractTests
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<LincleLinkDbContext> _factory;

    public SqliteInstanceRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _factory = CreateFactory(_connection);
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }

    protected override IInstanceRepository CreateRepository() => new SqliteInstanceRepository(_factory, NullLogger<SqliteInstanceRepository>.Instance);

    private static IDbContextFactory<LincleLinkDbContext> CreateFactory(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<LincleLinkDbContext>(o => o.UseSqlite(connection));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();
    }

    [Fact]
    public async Task Upsert_keeps_canonical_casing_when_saved_with_different_case()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create(
            "IIDX28",
            [new InstanceFile("f.bin", "", 1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")],
            ["dir"]));

        await repo.SaveAsync(Instance.Create(
            "iidx28",
            [new InstanceFile("g.bin", "", 2, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin")],
            ["other"]));

        (await repo.GetNamesAsync()).Should().Equal("IIDX28");
        var loaded = await repo.GetAsync("IIDX28");
        loaded.Should().NotBeNull();
        loaded!.FileList.Should().ContainSingle().Which.FileName.Should().Be("g.bin");
        loaded.DirectoryList.Should().Equal("other");
    }

    [Fact]
    public async Task Data_persists_across_factory_instances_on_a_real_db_file()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<LincleLinkDbContext>(
            o => o.UseSqlite(LincleLinkPersistence.ConnectionStringFor(Temp.Root)));
        var factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();

        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
        }

        var repo = new SqliteInstanceRepository(factory, NullLogger<SqliteInstanceRepository>.Instance);
        await repo.SaveAsync(Instance.Create(
            "persisted",
            [new InstanceFile("f.bin", "", 5, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")],
            ["x"]));

        File.Exists(Path.Combine(Temp.Root, LincleLinkPersistence.DatabaseFileName)).Should().BeTrue();

        // A fresh factory + repository over the same file sees the data.
        var services2 = new ServiceCollection();
        services2.AddDbContextFactory<LincleLinkDbContext>(
            o => o.UseSqlite(LincleLinkPersistence.ConnectionStringFor(Temp.Root)));
        var factory2 = services2.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();
        var repo2 = new SqliteInstanceRepository(factory2, NullLogger<SqliteInstanceRepository>.Instance);

        var loaded = await repo2.GetAsync("persisted");
        loaded.Should().NotBeNull();
        loaded!.FileList.Should().ContainSingle().Which.FileSize.Should().Be(5);
    }
}
