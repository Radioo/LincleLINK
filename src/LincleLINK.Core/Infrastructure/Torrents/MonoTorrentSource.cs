using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Torrents;
using MonoTorrent;
using MonoTorrent.BEncoding;

namespace LincleLINK.Core.Infrastructure.Torrents;

/// <summary>
/// MonoTorrent-backed <see cref="ITorrentSource"/>. v1 torrents only: hybrid or
/// v2-only (BEP 52) torrents are rejected with a clear message (plan 07 D1).
/// v1 piece hashes are read from the info dictionary's <c>pieces</c> blob via the
/// public BEncoding API (MonoTorrent's <c>PieceHashesV1</c> is internal).
/// </summary>
public sealed class MonoTorrentSource : ITorrentSource
{
    public async Task<TorrentData> LoadAsync(string torrentFilePath, CancellationToken ct = default)
    {
        var torrent = await Torrent.LoadAsync(torrentFilePath);

        if (torrent.InfoHashes.V2 is not null)
        {
            throw new TorrentNotSupportedException(
                "This torrent uses the v2 format (BEP 52), which is not supported yet.");
        }

        var hashes = ReadV1PieceHashes(torrentFilePath);

        var files = new List<TorrentFileData>(torrent.Files.Count);
        foreach (var file in torrent.Files)
        {
            // TorrentFile.Path uses the host separator on some platforms; the domain
            // contract for FullPath is canonical '/' form.
            files.Add(new TorrentFileData(PathNormalizer.Canonicalize(file.Path), file.Length));
        }

        return new TorrentData(torrent.Name, torrent.Size, torrent.PieceLength, hashes, files);
    }

    private static List<byte[]> ReadV1PieceHashes(string torrentFilePath)
    {
        using var fs = File.OpenRead(torrentFilePath);
        var root = (BEncodedDictionary)BEncodedValue.Decode(fs);

        if (!root.TryGetValue(new BEncodedString("info"), out var infoValue) || infoValue is not BEncodedDictionary info)
        {
            throw new TorrentNotSupportedException("Torrent has no info dictionary.");
        }

        if (!info.TryGetValue(new BEncodedString("pieces"), out var piecesValue) || piecesValue is not BEncodedString pieces)
        {
            throw new TorrentNotSupportedException("Torrent has no v1 piece hashes.");
        }

        var span = pieces.Span;
        var hashes = new List<byte[]>(span.Length / 20);
        for (var i = 0; i + 20 <= span.Length; i += 20)
        {
            hashes.Add(span.Slice(i, 20).ToArray());
        }

        return hashes;
    }
}
