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

public sealed class TorrentServiceTests
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
    public async Task CheckFiles_matches_instances_across_separator_styles()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, Files));
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());

        var result = await CreateService().CheckFilesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Matched.Should().Be(2);
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task CheckFiles_does_not_match_sibling_directory_sharing_prefix()
    {
        (string, byte[])[] files =
        [
            ("data/a.bin", new byte[] { 1, 2, 3, 4 }),
            ("database/a.bin", new byte[] { 1, 2, 3, 4 }),
        ];
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, files));
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());

        var result = await CreateService().CheckFilesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Matched.Should().Be(1);
        result.MatchedFilePaths.Should().Equal("a.bin");
    }

    [Fact]
    public async Task CheckFiles_with_wrong_relative_path_matches_nothing()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, Files));
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());

        var result = await CreateService().CheckFilesAsync(new TorrentCheckRequest("inst", "x.torrent", "wrong"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Matched.Should().Be(0);
    }

    [Fact]
    public async Task CheckFiles_load_failure_returns_error()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TorrentData>(_ => throw new TorrentNotSupportedException("no v2"));

        var result = await CreateService().CheckFilesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("v2");
    }

    [Fact]
    public async Task CheckPieces_all_match_yields_zero_bad_pieces()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, Files));
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());

        _store.GetPath("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns("C:\\db\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin");
        _store.GetPath("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin").Returns("C:\\db\\BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin");
        _fs.OpenRead("C:\\db\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(_ => new MemoryStream(Files[0].Item2));
        _fs.OpenRead("C:\\db\\BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin").Returns(_ => new MemoryStream(Files[1].Item2));

        var result = await CreateService().CheckPiecesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.PieceCountMismatch.Should().BeFalse();
        result.TotalPieces.Should().Be(2);
        result.MatchedPieces.Should().Be(2);
        result.Files.Should().HaveCount(2);
    }

    [Fact]
    public async Task LinkToTorrent_links_only_clean_piece_files()
    {
        var files = new List<TorrentFileCheck>
        {
            new("data/a.bin", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", new HashSet<long> { 0 }),
            new("data/b.bin", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin", new HashSet<long> { 1 }),
            new("data/bad.bin", null, new HashSet<long> { 2 }),           // no local match
            new("data/dirty.bin", "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.bin", new HashSet<long> { 1 }), // shares bad piece
        };

        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });
        _store.GetPath(Arg.Any<string>()).Returns(x => "C:\\db\\" + x[0]);

        var result = await CreateService().LinkToTorrentAsync(
            new LinkToTorrentRequest("C:\\dl", files, [1]),
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Linked.Should().Be(1); // only a.bin (clean piece 0)
        result.Skipped.Should().Be(3);

        var expectedDir = Path.GetDirectoryName(PathNormalizer.ToPlatformSeparators(Path.Combine("C:\\dl", "data/a.bin")))!;
        _fs.Received().CreateDirectory(expectedDir);
    }

    [Fact]
    public async Task LinkToTorrent_empty_download_path_is_error()
    {
        var result = await CreateService().LinkToTorrentAsync(new LinkToTorrentRequest("", [], []), ct: TestContext.Current.CancellationToken);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }
}
