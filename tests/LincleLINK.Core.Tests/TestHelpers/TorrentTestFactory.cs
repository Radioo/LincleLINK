using System.Security.Cryptography;
using LincleLINK.Core.Domain.Torrents;
using MonoTorrent.BEncoding;

namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>
/// Builds deterministic v1 .torrent fixtures and matching <see cref="TorrentData"/>
/// from in-memory files, so tests never need the network.
/// </summary>
public static class TorrentTestFactory
{
    public static string CreateTorrentFile(string path, int pieceLength, IReadOnlyList<(string RelPath, byte[] Data)> files)
    {
        var root = new BEncodedDictionary();
        root[new BEncodedString("announce")] = new BEncodedString("http://tracker.invalid/announce");

        var info = new BEncodedDictionary();
        info[new BEncodedString("name")] = new BEncodedString("fixture");
        info[new BEncodedString("piece length")] = new BEncodedNumber(pieceLength);

        var fileList = new BEncodedList();
        foreach (var file in files)
        {
            var fileDict = new BEncodedDictionary();
            fileDict[new BEncodedString("length")] = new BEncodedNumber(file.Data.Length);

            var pathList = new BEncodedList();
            foreach (var segment in file.RelPath.Split('/'))
            {
                pathList.Add(new BEncodedString(segment));
            }

            fileDict[new BEncodedString("path")] = pathList;
            fileList.Add(fileDict);
        }

        info[new BEncodedString("files")] = fileList;
        info[new BEncodedString("pieces")] = new BEncodedString(BuildPieceBlob(files, pieceLength));
        root[new BEncodedString("info")] = info;

        File.WriteAllBytes(path, root.Encode());
        return path;
    }

    public static TorrentData BuildTorrentData(int pieceLength, IReadOnlyList<(string RelPath, byte[] Data)> files)
    {
        var pieceHashes = ComputePieceHashes(files, pieceLength);
        var fileData = files
            .Select(f => new TorrentFileData(f.RelPath, f.Data.Length))
            .ToList();

        return new TorrentData("fixture", files.Sum(f => f.Data.Length), pieceLength, pieceHashes, fileData);
    }

    public static List<byte[]> ComputePieceHashes(IReadOnlyList<(string RelPath, byte[] Data)> files, int pieceLength)
    {
        var hashes = new List<byte[]>();
        var buffer = new byte[pieceLength];
        int filled = 0;

        foreach (var file in files)
        {
            foreach (var b in file.Data)
            {
                buffer[filled++] = b;
                if (filled == pieceLength)
                {
                    hashes.Add(SHA1.HashData(buffer));
                    filled = 0;
                }
            }
        }

        // Per BitTorrent v1 the final piece's hash covers only the remaining
        // bytes; zero-padding it here previously masked the same bug in
        // TorrentPieceVerifier (real torrents always failed their last piece).
        if (filled > 0)
        {
            hashes.Add(SHA1.HashData(buffer.AsSpan(0, filled)));
        }

        return hashes;
    }

    private static byte[] BuildPieceBlob(IReadOnlyList<(string RelPath, byte[] Data)> files, int pieceLength)
    {
        var hashes = ComputePieceHashes(files, pieceLength);
        var blob = new byte[hashes.Count * 20];
        for (var i = 0; i < hashes.Count; i++)
        {
            hashes[i].CopyTo(blob, i * 20);
        }

        return blob;
    }
}
