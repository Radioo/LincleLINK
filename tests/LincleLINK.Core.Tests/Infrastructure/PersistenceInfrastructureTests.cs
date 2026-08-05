using FluentAssertions;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Persistence;
using LincleLINK.Core.Infrastructure.Persistence.Migrations;
using LincleLINK.Core.Infrastructure.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace LincleLINK.Core.Tests.Infrastructure;

/// <summary>
/// Direct coverage for internal JSON normalization, the design-time context
/// factory, migration Down paths, and the entity row types.
/// </summary>
public sealed class PersistenceInfrastructureTests
{
    [Fact]
    public void InstanceJson_Normalize_repairs_null_collections_and_nulls()
    {
        var instance = new Instance
        {
            InstanceName = null!,
            TotalFileSizeString = null!,
            FileList = null!,
            DirectoryList = null!,
        };

        var normalized = InstanceJson.Normalize(instance);

        normalized.FileList.Should().BeEmpty();
        normalized.DirectoryList.Should().BeEmpty();
        normalized.InstanceName.Should().BeEmpty();
        normalized.TotalFileSizeString.Should().BeEmpty();
    }

    [Fact]
    public void InstanceJson_Options_respect_nullable_annotations()
    {
        InstanceJson.Options.RespectNullableAnnotations.Should().BeTrue();
        InstanceJson.Options.WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void Design_time_factory_creates_a_working_context()
    {
        var factory = new LincleLinkDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Should().NotBeNull();
    }

    [Fact]
    public void Entity_row_types_carry_data()
    {
        var file = new InstanceFileEntity
        {
            Id = 1,
            InstanceName = "IIDX28",
            Ordinal = 0,
            FileName = "a.bin",
            RelativePath = "sub",
            FileSize = 10,
            HashedFileName = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin",
        };
        var dir = new InstanceDirectoryEntity
        {
            Id = 2,
            InstanceName = "IIDX28",
            Ordinal = 0,
            Value = "sub",
        };

        file.Id.Should().Be(1);
        file.Ordinal.Should().Be(0);
        dir.Id.Should().Be(2);
        dir.Value.Should().Be("sub");
    }

    [Fact]
    public void Migration_Down_methods_run_without_error()
    {
        // The public Migrate API cannot target "before the first migration", so the
        // Down builders are exercised directly via reflection (Down is protected).
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        RunDown(new InitialCreate(), builder);
        RunDown(new AddGameDetection(), builder);
        RunDown(new AddDetectionConfidence(), builder);
    }

    private static void RunDown(Migration migration, MigrationBuilder builder)
    {
        var down = migration.GetType().GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Down method not found.");
        down.Invoke(migration, new object[] { builder });
    }

    [Fact]
    public async Task Migrate_rolls_back_to_an_earlier_migration()
    {
        // Applying, then migrating back to the first migration exercises
        // AddGameDetection.Down against a real database.
        var dataDir = Path.Combine(Path.GetTempPath(), "LincleLINK.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        try
        {
            var options = new DbContextOptionsBuilder<LincleLinkDbContext>()
                .UseSqlite(LincleLinkPersistence.ConnectionStringFor(dataDir))
                .Options;

            await using (var context = new LincleLinkDbContext(options))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            await using (var rollback = new LincleLinkDbContext(options))
            {
                await rollback.Database.MigrateAsync("20260802122044_InitialCreate", TestContext.Current.CancellationToken);
            }

            await using (var check = new LincleLinkDbContext(options))
            {
                var applied = await check.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
                applied.Should().Equal("20260802122044_InitialCreate");
            }
        }
        finally
        {
            // SQLite pools connections by default; release them so the db file is
            // not locked during cleanup.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, recursive: true);
            }
        }
    }
}
