using FluentAssertions;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

/// <summary>
/// Bulk-insert chunking, game-info round-trips and custom-logo branches of
/// <see cref="SqliteInstanceRepository"/>.
/// </summary>
public sealed class SqliteInstanceRepositoryCoverageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<LincleLinkDbContext> _factory;
    private readonly SqliteInstanceRepository _repo;

    public SqliteInstanceRepositoryCoverageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _factory = CreateFactory(_connection);
        using var context = _factory.CreateDbContext();
        context.Database.EnsureCreated();
        _repo = new SqliteInstanceRepository(_factory);
    }

    public void Dispose() => _connection.Dispose();

    private static IDbContextFactory<LincleLinkDbContext> CreateFactory(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<LincleLinkDbContext>(o => o.UseSqlite(connection));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<LincleLinkDbContext>>();
    }

    [Fact]
    public async Task BulkInsert_with_no_instances_is_a_noop()
    {
        await _repo.BulkInsertAsync([], TestContext.Current.CancellationToken);

        (await _repo.GetNamesAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task BulkInsert_with_multiple_files_and_dirs_preserves_chunk_order()
    {
        var instance = Instance.Create(
            "multi",
            [
                new InstanceFile("b.bin", "dir", 2, "B".PadRight(32, 'B') + ".bin"),
                new InstanceFile("a.bin", "dir", 3, "A".PadRight(32, 'A') + ".bin"),
                new InstanceFile("c.bin", "", 4, "C".PadRight(32, 'C') + ".bin"),
            ],
            ["z", "a", "m"]);

        await _repo.BulkInsertAsync([instance], TestContext.Current.CancellationToken);

        var loaded = await _repo.GetAsync("multi", TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.FileList.Select(f => f.FileName).Should().Equal("b.bin", "a.bin", "c.bin");
        loaded.DirectoryList.Should().Equal("z", "a", "m");
    }

    [Fact]
    public async Task Game_info_with_pe_identifier_roundtrips_as_xml_and_pe()
    {
        var instance = Instance.Create("game", [], []);
        instance.DetectedGame = new GameVersionInfo(
            "KFC", "SOUND VOLTEX", "2013060500",
            "kfc-5a01c0a8_1000", null, "SDVX/SDVX_II_logo", DetectionConfidence.XmlAndPe);

        await _repo.SaveAsync(instance, TestContext.Current.CancellationToken);
        var loaded = await _repo.GetAsync("game", TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.DetectedGame.Should().NotBeNull();
        loaded.DetectedGame!.GameCode.Should().Be("KFC");
        loaded.DetectedGame.GameTitle.Should().Be("SOUND VOLTEX");
        loaded.DetectedGame.DateCode.Should().Be("2013060500");
        loaded.DetectedGame.PeIdentifier.Should().Be("kfc-5a01c0a8_1000");
        loaded.DetectedGame.LogoKey.Should().Be("SDVX/SDVX_II_logo");
        loaded.DetectedGame.Confidence.Should().Be(DetectionConfidence.XmlAndPe);

        var summary = (await _repo.GetSummariesAsync(TestContext.Current.CancellationToken)).Single();
        summary.DetectedGame.Should().NotBeNull();
        summary.DetectedGame!.Confidence.Should().Be(DetectionConfidence.XmlAndPe);
    }

    [Fact]
    public async Task Game_info_without_pe_identifier_is_xml_confidence()
    {
        var instance = Instance.Create("game", [], []);
        instance.DetectedGame = new GameVersionInfo(
            "LDJ", "beatmania IIDX", "2022101900",
            null, "beatmania IIDX 30 RESIDENT", "IIDX/AC_RESIDENT_logo", DetectionConfidence.Xml);

        await _repo.SaveAsync(instance, TestContext.Current.CancellationToken);
        var loaded = await _repo.GetAsync("game", TestContext.Current.CancellationToken);

        loaded!.DetectedGame!.Confidence.Should().Be(DetectionConfidence.Xml);
    }

    [Fact]
    public async Task DllOnly_confidence_survives_the_roundtrip()
    {
        // Regression: confidence used to initialize to Xml unconditionally, and
        // the persistence layer re-derived it from PeIdentifier presence, so a
        // DLL-only detection was reported as config-verified after reload.
        var instance = Instance.Create("game", [], []);
        instance.DetectedGame = new GameVersionInfo(
            "KFC", "SOUND VOLTEX", null, null, null, "SDVX/SDVX_BOOTH_logo", DetectionConfidence.DllOnly);

        await _repo.SaveAsync(instance, TestContext.Current.CancellationToken);
        var loaded = await _repo.GetAsync("game", TestContext.Current.CancellationToken);

        loaded!.DetectedGame!.Confidence.Should().Be(DetectionConfidence.DllOnly);

        var summary = (await _repo.GetSummariesAsync(TestContext.Current.CancellationToken)).Single();
        summary.DetectedGame!.Confidence.Should().Be(DetectionConfidence.DllOnly);
    }

    [Fact]
    public async Task SetCustomLogo_updates_only_when_the_instance_exists()
    {
        await _repo.SaveAsync(Instance.Create("A", [], []), TestContext.Current.CancellationToken);

        await _repo.SetCustomLogoAsync("A", "custom", TestContext.Current.CancellationToken);

        var summary = (await _repo.GetSummariesAsync(TestContext.Current.CancellationToken)).Single();
        summary.CustomLogoSource.Should().Be("custom");

        await _repo.SetCustomLogoAsync("missing", "custom", TestContext.Current.CancellationToken);

        var loaded = await _repo.GetAsync("A", TestContext.Current.CancellationToken);
        loaded!.CustomLogoSource.Should().Be("custom");
    }

    [Fact]
    public async Task SetCustomLogo_rejects_invalid_names()
    {
        var act = () => _repo.SetCustomLogoAsync("bad/name", "custom");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
