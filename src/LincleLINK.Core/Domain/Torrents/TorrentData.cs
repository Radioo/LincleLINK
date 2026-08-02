namespace LincleLINK.Core.Domain.Torrents;

/// <summary>Pure torrent metadata parsed by <c>ITorrentSource</c> (v1 piece hashes only).</summary>
public sealed record TorrentData(
    string Name,
    long TotalSize,
    int PieceLength,
    IReadOnlyList<byte[]> PieceHashes,
    IReadOnlyList<TorrentFileData> Files);

/// <summary>A file inside a torrent; <c>FullPath</c> uses '/' separators (BEP).</summary>
public sealed record TorrentFileData(string FullPath, long Length);
