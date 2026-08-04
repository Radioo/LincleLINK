using System.Data.Common;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Persistence;
using LincleLINK.Core.Tests.TestHelpers;
using LincleLINK.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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

    /// <summary>
    /// Service wired to a substituted repository so verification/insertion failure
    /// paths can be forced; the real factory still applies the schema migrations.
    /// </summary>
    private StorageMigrationService CreateService(IInstanceRepository repository)
        => new(_paths, repository, _factory);

    private void WriteInstanceJson(string name, string json)
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        File.WriteAllText(Path.Combine(_paths.InstanceDirectory, name + ".json"), json);
    }

    private static string MinimalJson(string name)
        => $$"""{"Name":"{{name}}","TotalFileSize":0,"TotalFileCount":0,"TotalFileSizeString":"0 B"}""";

    // Mirrors StorageMigrationService.BulkFlushThreshold: the test needs to push a
    // manifest past a flush so it lands in a different batch than the name it dupes.
    private const int BulkFlushThreshold = 50;

    [Fact]
    public void NeedsMigration_with_empty_instance_dir_returns_false()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);

        CreateService().NeedsMigration().Should().BeFalse();
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

    [Fact]
    public async Task MigrateAsync_without_instance_directory_returns_empty_result()
    {
        // Mirror production startup (paths.EnsureCreated) so the data root exists
        // but no legacy instance/ folder does.
        _paths.EnsureCreated();
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Quarantined.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureSchemaAsync_creates_schema_on_fresh_install()
    {
        // Fresh install: no instance/ directory and no migration run, yet the
        // schema must exist so the first repository query does not hit a missing
        // table (startup calls EnsureSchemaAsync unconditionally).
        _paths.EnsureCreated();
        var service = CreateService();

        await service.EnsureSchemaAsync();

        File.Exists(Path.Combine(_paths.DataDirectory, LincleLinkPersistence.DatabaseFileName)).Should().BeTrue();
        var loaded = await _repository.GetAllAsync();
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_invalid_name_and_continues()
    {
        // A reserved device name is rejected by the repository with an
        // ArgumentException; it must quarantine that manifest rather than abort
        // the loop and strand the remaining files.
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        WriteInstanceJson("bad", """{"Name":"CON","TotalFileSize":0,"TotalFileCount":0,"TotalFileSizeString":"0 B"}""");
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(1);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("CON");
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "bad.json")).Should().BeTrue();

        var loaded = await _repository.GetAsync("IIDX28");
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task MigrateAsync_reports_log_and_progress_on_success()
    {
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService();
        var log = new List<string>();
        var percents = new List<double>();

        var result = await service.MigrateAsync(
            new SynchronousProgress<string>(log.Add),
            new SynchronousProgress<double>(percents.Add));

        result.Migrated.Should().Be(1);
        log.Should().Contain("Migrated IIDX28");
        log.Should().Contain("Migration finished: 1 migrated, 0 already present, 0 quarantined.");
        percents.Last().Should().Be(100);
    }

    [Fact]
    public async Task MigrateAsync_logs_quarantine_and_progress()
    {
        WriteInstanceJson("bad", "{ this is not json");
        var service = CreateService();
        var log = new List<string>();
        var percents = new List<double>();

        var result = await service.MigrateAsync(
            new SynchronousProgress<string>(log.Add),
            new SynchronousProgress<double>(percents.Add));

        result.Quarantined.Should().Be(1);
        log.Should().Contain(line => line.Contains("Quarantined bad"));
        percents.Last().Should().Be(100);
    }

    [Fact]
    public async Task MigrateAsync_quarantines_when_verification_fails()
    {
        // The bulk insert "succeeds" but the follow-up existence check reports the
        // row is absent, so the manifest must be quarantined (not deleted).
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService(repository);

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(0);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Verification failed after writing");
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "IIDX28.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_falls_back_to_per_instance_save_when_bulk_insert_fails()
    {
        // A bulk insert failure must not strand the batch: each manifest is retried
        // individually so a single bad instance is quarantined, not the whole chunk.
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("bulk failed"));
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService(repository);

        var result = await service.MigrateAsync();

        await repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
        result.Migrated.Should().Be(0);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Verification failed after writing");
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "IIDX28.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_null_json_content()
    {
        WriteInstanceJson("bad", "null");
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Instance JSON was null");
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "bad.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_falls_back_when_bulk_insert_throws_db_exception()
    {
        // SQLite database failures (locked/corrupt DB, constraint violation, disk
        // full) surface as SqliteException/DbUpdateException, not the IOException the
        // original filter caught. They must trigger the same per-instance fallback so
        // one bad manifest is quarantined instead of stranding the whole batch.
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TestDbException("database is locked"));
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService(repository);

        var result = await service.MigrateAsync();

        await repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
        result.Migrated.Should().Be(0);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Verification failed after writing");
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "IIDX28.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_duplicate_inner_name_in_same_batch()
    {
        // Two manifests whose inner Names collide (identical, or case variants) share
        // one primary-key value. The file names must stay distinct on every platform:
        // on a case-insensitive filesystem (Windows, default macOS) IIDX28.json and
        // iidx28.json are the same file, so the collision is expressed through the
        // inner Name, not the file name.
        WriteInstanceJson("first", TestData.V2InstanceJson);
        WriteInstanceJson("second", MinimalJson("iidx28"));
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(1);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Duplicate manifest name");
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "second.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_quarantines_invalid_inner_name_without_degrading_bulk_path()
    {
        // A reserved inner Name (CON) in an otherwise valid manifest must be
        // quarantined during the parse phase, so the valid manifest still goes
        // through the fast bulk path (one BulkInsertAsync, zero per-instance saves).
        var recording = new RecordingRepository(_repository);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        WriteInstanceJson("foo", """{"Name":"CON","TotalFileSize":0,"TotalFileCount":0,"TotalFileSizeString":"0 B"}""");
        var service = CreateService(recording);

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(1);
        result.Quarantined.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("CON");
        recording.BulkInsertCalls.Should().Be(1);
        recording.SaveCalls.Should().Be(0);
        File.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt", "foo.json")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_skips_name_already_flushed_in_an_earlier_batch()
    {
        // A name that was flushed in an earlier chunk must be skipped when it
        // reappears in a later one (the existing-key snapshot is updated after each
        // flush); otherwise the duplicate would be re-inserted and trip the unique
        // NameKey, silently overwriting the earlier instance via the upsert fallback.
        for (var i = 0; i < BulkFlushThreshold; i++)
        {
            WriteInstanceJson($"IIDX{i:D3}", MinimalJson($"IIDX{i:D3}"));
        }

        WriteInstanceJson("IIDX999", MinimalJson("IIDX000"));
        var service = CreateService();

        var result = await service.MigrateAsync();

        result.Migrated.Should().Be(BulkFlushThreshold);
        result.Skipped.Should().Be(1);
        result.Quarantined.Should().Be(0);
        result.Errors.Should().BeEmpty();
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAsync_does_not_reinsert_when_finalize_verification_fails()
    {
        // The bulk insert succeeds but the post-write verification (ExistsAsync) then
        // hits a DB error. That must not be routed through the "bulk insert failed;
        // retry individually" path, which would re-write the whole batch for nothing.
        // The finalize step lives outside the insert's try, so the failure propagates
        // and the batch is left for the idempotent next launch.
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new TestDbException("db gone")));
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService(repository);

        var act = () => service.MigrateAsync();

        await act.Should().ThrowAsync<TestDbException>();
        await repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateAsync_aborts_not_quarantines_on_db_failure_in_fallback()
    {
        // A DB-wide failure (locked/corrupt DB) during the per-instance fallback must
        // abort the migration (the JSON stays put and it is re-offered next launch)
        // rather than move the batch to instance-corrupt/, which would strand it
        // outside the migration path after a transient lock.
        var repository = Substitute.For<IInstanceRepository>();
        repository.BulkInsertAsync(Arg.Any<IReadOnlyList<Instance>>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TestDbException("database is locked"));
        repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TestDbException("database is locked"));
        WriteInstanceJson("IIDX28", TestData.V2InstanceJson);
        var service = CreateService(repository);

        var act = () => service.MigrateAsync();

        await act.Should().ThrowAsync<TestDbException>();
        Directory.GetFiles(_paths.InstanceDirectory, "*.json").Should().ContainSingle();
        Directory.Exists(Path.Combine(_paths.InstanceDirectory, "instance-corrupt")).Should().BeFalse();
    }

    /// <summary>Concrete <see cref="DbException"/> so the migration fallback filter can be exercised.</summary>
    private sealed class TestDbException : DbException
    {
        public TestDbException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Delegates to the real SQLite repository but counts the migration write paths,
    /// so tests can assert the bulk insert is used (and the slow per-instance path
    /// is not) for a given input.
    /// </summary>
    private sealed class RecordingRepository : IInstanceRepository
    {
        private readonly IInstanceRepository _inner;

        public RecordingRepository(IInstanceRepository inner) => _inner = inner;

        public int BulkInsertCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default) => _inner.GetNamesAsync(ct);
        public Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default) => _inner.GetAllAsync(ct);
        public Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default) => _inner.GetAllHashedFileNamesAsync(ct);
        public Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default) => _inner.GetSummariesAsync(ct);
        public Task<Instance?> GetAsync(string name, CancellationToken ct = default) => _inner.GetAsync(name, ct);
        public Task<bool> ExistsAsync(string name, CancellationToken ct = default) => _inner.ExistsAsync(name, ct);
        public Task<long> GetUniqueSizeAsync(string name, CancellationToken ct = default) => _inner.GetUniqueSizeAsync(name, ct);
        public Task<bool> DeleteAsync(string name, CancellationToken ct = default) => _inner.DeleteAsync(name, ct);

        public async Task SaveAsync(Instance instance, CancellationToken ct = default)
        {
            SaveCalls++;
            await _inner.SaveAsync(instance, ct);
        }

        public async Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default)
        {
            BulkInsertCalls++;
            await _inner.BulkInsertAsync(instances, ct);
        }

        public Task SetCustomLogoAsync(string name, string? logoSource, CancellationToken ct = default)
            => _inner.SetCustomLogoAsync(name, logoSource, ct);
    }
}
