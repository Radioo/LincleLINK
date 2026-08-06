using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Torrents;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Remaining branches of <see cref="TorrentService"/>: missing instance lookups,
/// piece-count mismatch, unsafe/existing/failed link targets, and the local-file
/// map skipping torrent files outside the relative prefix.
/// </summary>
public sealed class TorrentServiceCoverageTests
{
    private readonly ITorrentSource _source = Substitute.For<ITorrentSource>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();

    private TorrentService CreateService() => new(_source, _repository, _store, _hardLinker, _fs);

    private static readonly (string, byte[])[] Files =
    [
        ("data/a.bin", new byte[] { 1, 2, 3, 4 }),
        ("data/b.bin", new byte[] { 5, 6 }),
    ];

    private static Instance SampleInstance() => Instance.Create(
        "inst",
        [
            new InstanceFile("a.bin", "", 4, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
            new InstanceFile("b.bin", "", 2, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
        ],
        [""]);

    [Fact]
    public async Task CheckPiecesResult_with_bad_pieces_is_constructible()
    {
        var fileCheck = new TorrentFileCheck("data/a.bin", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", new HashSet<long> { 0 });

        var result = new CheckPiecesResult(true, null, false, 0, 1, [3L, 4L], [fileCheck]);

        result.BadPieces.Should().Equal(3L, 4L);
        result.Files.Should().ContainSingle();
    }

    [Fact]
    public async Task CheckFiles_missing_instance_returns_error()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, Files));
        _repository.GetAsync("nope", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().CheckFilesAsync(new TorrentCheckRequest("nope", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckPieces_load_failure_returns_error()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TorrentData>(_ => throw new TorrentNotSupportedException("no v2"));

        var result = await CreateService().CheckPiecesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("v2");
    }

    [Fact]
    public async Task CheckPieces_missing_instance_returns_error()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, Files));
        _repository.GetAsync("nope", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().CheckPiecesAsync(new TorrentCheckRequest("nope", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckPieces_piece_count_mismatch_returns_error()
    {
        var torrent = TorrentTestFactory.BuildTorrentData(4, Files) with
        {
            PieceHashes = [.. TorrentTestFactory.ComputePieceHashes(Files, 4).Take(1)],
        };
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(torrent);
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());

        var result = await CreateService().CheckPiecesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.PieceCountMismatch.Should().BeTrue();
        result.Error.Should().Contain("Piece count does not match");
    }

    [Fact]
    public async Task CheckPieces_ignores_torrent_files_outside_the_relative_prefix()
    {
        (string, byte[])[] files =
        [
            ("data/a.bin", new byte[] { 1, 2, 3, 4 }),
            ("other/c.bin", new byte[] { 9, 9 }),
        ];
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, files));
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _store.GetPath(Arg.Any<string>()).Returns(x => "C:\\db\\" + x[0]);

        var result = await CreateService().CheckPiecesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        // Only the in-prefix file is mapped; "other/c.bin" is skipped by the map.
        result.Files.Should().ContainSingle(f => f.TorrentPath == "data/a.bin");
    }

    [Fact]
    public async Task LinkToTorrent_skips_unsafe_paths_and_logs()
    {
        var files = new List<TorrentFileCheck>
        {
            new(@"..\..\evil.bin", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", new HashSet<long> { 0 }),
        };
        var logs = new List<string>();

        var result = await CreateService().LinkToTorrentAsync(
            new LinkToTorrentRequest("C:\\dl", files, []),
            new SynchronousProgress<string>(logs.Add),
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Skipped.Should().Be(1);
        logs.Should().Contain(m => m.Contains("unsafe path"));
    }

    [Fact]
    public async Task LinkToTorrent_skips_existing_targets()
    {
        var files = new List<TorrentFileCheck>
        {
            new("data/a.bin", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", new HashSet<long> { 0 }),
        };
        _fs.FileExists(Arg.Any<string>()).Returns(true);

        var result = await CreateService().LinkToTorrentAsync(new LinkToTorrentRequest("C:\\dl", files, []), ct: TestContext.Current.CancellationToken);

        result.Linked.Should().Be(0);
        result.Skipped.Should().Be(1);
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public async Task LinkToTorrent_logs_and_skips_on_link_failure()
    {
        var files = new List<TorrentFileCheck>
        {
            new("data/a.bin", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", new HashSet<long> { 0 }),
        };
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = "Access denied.";
            return false;
        });
        var logs = new List<string>();

        var result = await CreateService().LinkToTorrentAsync(
            new LinkToTorrentRequest("C:\\dl", files, []),
            new SynchronousProgress<string>(logs.Add),
            ct: TestContext.Current.CancellationToken);

        result.Linked.Should().Be(0);
        result.Skipped.Should().Be(1);
        logs.Should().Contain(m => m.Contains("Access denied."));
    }
}
