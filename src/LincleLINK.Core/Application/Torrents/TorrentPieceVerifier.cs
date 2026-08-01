using System.Security.Cryptography;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Torrents;

namespace LincleLINK.Core.Application.Torrents;

/// <summary>
/// A per-torrent-file verification record: the canonical torrent path, the db
/// hashed file name when a local match exists (else null), and the piece indices
/// the file occupies (pieces can be shared across file boundaries).
/// </summary>
public sealed record TorrentFileCheck(string TorrentPath, string? HashedFileName, IReadOnlySet<long> Pieces);

public sealed record VerificationResult(
    bool PieceCountMismatch,
    IReadOnlyList<long> BadPieceIndices,
    IReadOnlyList<TorrentFileCheck> Files);

/// <summary>
/// Streaming piece verification with v2 semantics (plan 07 §4): files are walked in
/// torrent order; a matched file contributes its <c>db/</c> bytes, an unmatched file
/// contributes zeros. Memory is bounded by one piece buffer (fixes v2's full-file and
/// giant zero-array allocations).
/// </summary>
public sealed class TorrentPieceVerifier
{
    private readonly TorrentData _torrent;
    private readonly IReadOnlyDictionary<string, string> _localFiles; // canonical torrent path → db file path

    public TorrentPieceVerifier(TorrentData torrent, IReadOnlyDictionary<string, string> localFiles)
    {
        _torrent = torrent;
        _localFiles = localFiles;
    }

    public async Task<VerificationResult> VerifyAsync(
        IFileSystem fileSystem,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        long expectedPieces = _torrent.PieceLength > 0
            ? (_torrent.TotalSize + _torrent.PieceLength - 1) / _torrent.PieceLength
            : 0;

        if (expectedPieces != _torrent.PieceHashes.Count)
        {
            return new VerificationResult(true, [], []);
        }

        var buffer = new byte[_torrent.PieceLength];
        int filled = 0;              // bytes filled in the current piece buffer
        long pieceIndex = 0;         // current piece being built
        var badPieces = new List<long>();
        var fileChecks = new List<TorrentFileCheck>(_torrent.Files.Count);

        double step = _torrent.Files.Count == 0 ? 0 : 100d / _torrent.Files.Count;
        int fileIndex = 0;

        foreach (var file in _torrent.Files)
        {
            ct.ThrowIfCancellationRequested();

            var canonicalPath = PathNormalizer.Canonicalize(file.FullPath);
            _localFiles.TryGetValue(canonicalPath, out var dbPath);

            var piecesForFile = new HashSet<long>();

            await using var stream = dbPath is not null ? fileSystem.OpenRead(dbPath) : null;

            long remaining = file.Length;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int space = _torrent.PieceLength - filled;
                int toWrite = (int)Math.Min(space, remaining);

                piecesForFile.Add(pieceIndex);

                if (stream is not null)
                {
                    int read = 0;
                    while (read < toWrite)
                    {
                        int n = await stream.ReadAsync(buffer.AsMemory(filled + read, toWrite - read), ct);
                        if (n <= 0)
                        {
                            break; // file shorter than recorded size: pad with zeros
                        }

                        read += n;
                    }

                    if (read < toWrite)
                    {
                        Array.Clear(buffer, filled + read, toWrite - read);
                    }
                }
                else
                {
                    Array.Clear(buffer, filled, toWrite);
                }

                filled += toWrite;
                remaining -= toWrite;

                if (filled == _torrent.PieceLength)
                {
                    HashAndCheck(buffer, pieceIndex, badPieces);
                    filled = 0;
                    pieceIndex++;
                }
            }

            fileChecks.Add(new TorrentFileCheck(
                canonicalPath,
                dbPath is not null ? Path.GetFileName(dbPath) : null,
                piecesForFile));

            percent?.Report(++fileIndex * step);
        }

        // Final partial piece (zero-padded).
        if (filled > 0)
        {
            Array.Clear(buffer, filled, _torrent.PieceLength - filled);
            HashAndCheck(buffer, pieceIndex, badPieces);
        }

        return new VerificationResult(false, badPieces, fileChecks);
    }

    private void HashAndCheck(byte[] piece, long index, List<long> badPieces)
    {
        var hash = SHA1.HashData(piece);
        if (!hash.AsSpan().SequenceEqual(_torrent.PieceHashes[(int)index]))
        {
            badPieces.Add(index);
        }
    }
}
