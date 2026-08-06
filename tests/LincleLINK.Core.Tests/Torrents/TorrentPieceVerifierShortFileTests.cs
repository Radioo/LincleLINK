using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Torrents;

/// <summary>
/// A local db file shorter than the torrent's recorded length must be zero-padded
/// (EOF mid-piece), not crash or spin.
/// </summary>
public sealed class TorrentPieceVerifierShortFileTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IFileSystem _fs = new FileSystem();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Shorter_local_file_is_zero_padded_and_its_piece_is_bad()
    {
        // Torrent declares data/a.bin as 4 bytes; the db copy only holds 2.
        var files = new[] { ("data/a.bin", new byte[] { 1, 2, 3, 4 }) };
        var torrent = TorrentTestFactory.BuildTorrentData(4, files);

        var dbPath = _temp.CreateFile("a.bin", new byte[] { 1, 2 });
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["data/a.bin"] = dbPath };

        var result = await new TorrentPieceVerifier(torrent, map).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.PieceCountMismatch.Should().BeFalse();
        // {1,2,0,0} does not match the recorded hash of {1,2,3,4}.
        result.BadPieceIndices.Should().Contain(0);
    }

    [Fact]
    public async Task Shorter_local_file_pads_only_the_missing_tail()
    {
        // 6-byte file across two pieces; the local copy holds only 5 bytes, so the
        // tail of the last piece is zero-padded while the first piece still matches.
        var files = new[] { ("data/a.bin", new byte[] { 1, 2, 3, 4, 5, 6 }) };
        var torrent = TorrentTestFactory.BuildTorrentData(4, files);

        var dbPath = _temp.CreateFile("a.bin", new byte[] { 1, 2, 3, 4, 5 });
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["data/a.bin"] = dbPath };

        var result = await new TorrentPieceVerifier(torrent, map).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.BadPieceIndices.Should().Contain(1); // piece {5,0,0,0} != {5,6}
        result.BadPieceIndices.Should().NotContain(0);
    }
}
