using System.Diagnostics;
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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// The UI never freezes (CLAUDE.md): the torrent operations parse a torrent, match
/// it against an entry, hash gigabytes and create links. None of that may run on
/// the thread that started them, and a torrent of a whole game must match in
/// seconds, not hours.
/// </summary>
public sealed class TorrentServiceUiThreadTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin";

    private static readonly (string, byte[])[] Files =
    [
        ("data/a.bin", new byte[] { 1, 2, 3, 4 }),
        ("data/b.bin", new byte[] { 5, 6 }),
    ];

    private readonly UiThreadStandIn _ui = new();
    private readonly ITorrentSource _source = Substitute.For<ITorrentSource>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();

    public TorrentServiceUiThreadTests()
    {
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ui.Note("torrent parse");
            return TorrentTestFactory.BuildTorrentData(4, Files);
        });
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "inst",
            [new InstanceFile("a.bin", "", 4, HashA), new InstanceFile("b.bin", "", 2, HashB)],
            [""]));
        _store.GetPath(Arg.Any<string>()).Returns(call => "/db/" + call.Arg<string>()!);
        _fs.OpenRead(Arg.Any<string>()).Returns(call =>
        {
            _ui.Note("open stored file");
            return new MemoryStream(call.Arg<string>()!.EndsWith(HashA, StringComparison.Ordinal) ? Files[0].Item2 : Files[1].Item2);
        });
        _fs.When(f => f.CreateDirectory(Arg.Any<string>())).Do(_ => _ui.Note("create directory"));
        _fs.FileExists(Arg.Any<string>()).Returns(_ =>
        {
            _ui.Note("file exists");
            return false;
        });
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<string?>()).Returns(_ =>
        {
            _ui.Note("link");
            return true;
        });
    }

    private TorrentService CreateService()
        => new(_source, _repository, _store, _hardLinker, _fs, NullLogger<TorrentService>.Instance);

    [Fact]
    public async Task Checking_files_parses_and_matches_off_the_ui_thread()
    {
        var result = await _ui.RunAsync(() => CreateService().CheckFilesAsync(
            new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken));

        result.Matched.Should().Be(2);
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    [Fact]
    public async Task Checking_pieces_reads_and_hashes_off_the_ui_thread()
    {
        var result = await _ui.RunAsync(() => CreateService().CheckPiecesAsync(
            new TorrentCheckRequest("inst", "x.torrent", "data"), ct: TestContext.Current.CancellationToken));

        result.Success.Should().BeTrue();
        result.MatchedPieces.Should().Be(result.TotalPieces);
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    [Fact]
    public async Task Linking_to_a_torrent_touches_the_disk_off_the_ui_thread()
    {
        var request = new LinkToTorrentRequest(
            Path.Combine(Path.GetTempPath(), "dl"),
            [new TorrentFileCheck("data/a.bin", HashA, new HashSet<long> { 0 }), new TorrentFileCheck("data/b.bin", HashB, new HashSet<long> { 1 })],
            []);

        var result = await _ui.RunAsync(() => CreateService().LinkToTorrentAsync(request, ct: TestContext.Current.CancellationToken));

        result.Linked.Should().Be(2);
        _ui.RanOnUiThread.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_operation_says_what_it_is_doing_from_the_start()
    {
        var lines = new List<string>();
        var log = new InlineProgress<string>(lines.Add);

        await CreateService().CheckFilesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), log, ct: TestContext.Current.CancellationToken);
        var afterCheckFiles = lines.ToList();
        lines.Clear();
        await CreateService().CheckPiecesAsync(new TorrentCheckRequest("inst", "x.torrent", "data"), log, ct: TestContext.Current.CancellationToken);

        // Parsing a torrent has no steps to count, so it has to be named.
        afterCheckFiles.First().Should().StartWith("Reading ");
        lines.First().Should().StartWith("Reading ");
    }

    [Fact]
    public async Task Matching_a_whole_game_torrent_against_a_large_entry_takes_seconds_not_hours()
    {
        const int count = 20_000;
        var instanceFiles = new List<InstanceFile>(count);
        var torrentFiles = new List<TorrentFileData>(count);
        for (var i = 0; i < count; i++)
        {
            instanceFiles.Add(new InstanceFile($"{i:D6}.ifs", $@"graphic\{i / 100:D4}", 10, $"{i:X32}.ifs"));
            torrentFiles.Add(new TorrentFileData($"game/data/graphic/{i / 100:D4}/{i:D6}.ifs", 10));
        }

        _repository.GetAsync("big", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("big", instanceFiles, []));
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentData("game", count * 10L, 16384, [], torrentFiles));

        var clock = Stopwatch.StartNew();
        var result = await CreateService().CheckFilesAsync(
            new TorrentCheckRequest("big", "x.torrent", "game/data"), ct: TestContext.Current.CancellationToken);

        // Comparing every torrent file with every entry file is 400 million path
        // comparisons here and 15 billion for a real game.
        result.Matched.Should().Be(count);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Of_two_entry_files_with_the_same_path_and_size_the_first_one_still_wins()
    {
        _repository.GetAsync("dupes", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "dupes",
            [
                new InstanceFile("a.bin", "", 9, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.bin"), // same path, other size
                new InstanceFile("a.bin", "", 4, HashA),
                new InstanceFile("a.bin", "", 4, HashB),
            ],
            [""]));
        var opened = new List<string>();
        _fs.OpenRead(Arg.Any<string>()).Returns(call =>
        {
            lock (opened)
            {
                opened.Add(call.Arg<string>()!);
            }

            return new MemoryStream(Files[0].Item2);
        });
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TorrentTestFactory.BuildTorrentData(4, [Files[0]]));

        await CreateService().CheckPiecesAsync(
            new TorrentCheckRequest("dupes", "x.torrent", "data"), ct: TestContext.Current.CancellationToken);

        opened.Should().Equal("/db/" + HashA);
    }

    /// <summary>Reports inline; <see cref="Progress{T}"/> would post to a context and arrive late.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
