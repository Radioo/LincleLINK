using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Torrents;

public sealed class TorrentPieceVerifierTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IFileSystem _fs = new FileSystem();

    public void Dispose() => _temp.Dispose();

    private static readonly (string, byte[])[] Files =
    [
        ("data/a.bin", new byte[] { 1, 2, 3, 4 }),
        ("data/b.bin", new byte[] { 5, 6, 7, 8, 9 }),
        ("data/c.bin", new byte[] { 10 }),
    ];

    private Dictionary<string, string> WriteLocalFiles()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (relPath, data) in Files)
        {
            var dbPath = _temp.CreateFile("db_" + relPath.Replace('/', '_'), data);
            map[relPath] = dbPath;
        }

        return map;
    }

    [Fact]
    public void Final_partial_piece_is_hashed_unpadded_per_spec()
    {
        // BitTorrent v1: the last piece's hash covers only the remaining bytes.
        // Stream = 1..10 with piece length 4, so the final piece is {9, 10}.
        var hashes = TorrentTestFactory.ComputePieceHashes(Files, 4);

        hashes[^1].Should().Equal(System.Security.Cryptography.SHA1.HashData(new byte[] { 9, 10 }));
    }

    [Fact]
    public async Task All_matching_files_yield_no_bad_pieces()
    {
        var torrent = TorrentTestFactory.BuildTorrentData(4, Files);
        var verifier = new TorrentPieceVerifier(torrent, WriteLocalFiles());

        var result = await verifier.VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.PieceCountMismatch.Should().BeFalse();
        result.BadPieceIndices.Should().BeEmpty();
        result.Files.Should().HaveCount(3);
        result.Files[0].HashedFileName.Should().NotBeNull();
    }

    [Fact]
    public async Task Tampered_file_marks_its_pieces_bad()
    {
        var torrent = TorrentTestFactory.BuildTorrentData(4, Files);
        var map = WriteLocalFiles();
        // Corrupt the db bytes for data/a.bin (piece 0) only.
        await File.WriteAllBytesAsync(map["data/a.bin"], new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, TestContext.Current.CancellationToken);

        var result = await new TorrentPieceVerifier(torrent, map).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.BadPieceIndices.Should().Contain(0);
    }

    [Fact]
    public async Task Missing_file_yields_zero_hash_bad_pieces_but_other_files_pass()
    {
        var torrent = TorrentTestFactory.BuildTorrentData(4, Files);
        var map = WriteLocalFiles();
        map.Remove("data/b.bin"); // treat b as missing → zeros

        var result = await new TorrentPieceVerifier(torrent, map).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        // b.bin occupies pieces 1..2 (bytes 4..8). All its pieces must be bad.
        result.BadPieceIndices.Should().Contain(new[] { 1L, 2L });
        result.Files.First(f => f.TorrentPath == "data/b.bin").HashedFileName.Should().BeNull();
    }

    [Fact]
    public async Task Piece_count_mismatch_is_detected()
    {
        var files = Files.Select(f => (f.Item1, f.Item2)).ToArray();
        var torrent = TorrentTestFactory.BuildTorrentData(4, files) with { PieceHashes = [.. TorrentTestFactory.ComputePieceHashes(files, 4).Take(1)] };

        var result = await new TorrentPieceVerifier(torrent, WriteLocalFiles()).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.PieceCountMismatch.Should().BeTrue();
        result.BadPieceIndices.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_positive_piece_length_is_a_mismatch_not_a_loop()
    {
        var files = Files.Select(f => (f.Item1, f.Item2)).ToArray();
        var torrent = TorrentTestFactory.BuildTorrentData(4, files) with { PieceLength = 0 };

        var result = await new TorrentPieceVerifier(torrent, WriteLocalFiles()).VerifyAsync(_fs, ct: TestContext.Current.CancellationToken);

        result.PieceCountMismatch.Should().BeTrue();
        result.BadPieceIndices.Should().BeEmpty();
        result.Files.Should().BeEmpty();
    }
}
