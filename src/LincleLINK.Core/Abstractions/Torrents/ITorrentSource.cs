using LincleLINK.Core.Domain.Torrents;

namespace LincleLINK.Core.Abstractions.Torrents;

/// <summary>
/// Parses a .torrent file into v1 metadata. Throws
/// <see cref="TorrentNotSupportedException"/> for v2/hybrid (BEP 52) torrents.
/// </summary>
public interface ITorrentSource
{
    Task<TorrentData> LoadAsync(string torrentFilePath, CancellationToken ct = default);
}

/// <summary>Thrown when a torrent uses a format this build does not support (v2/hybrid).</summary>
public sealed class TorrentNotSupportedException : NotSupportedException
{
    public TorrentNotSupportedException(string message)
        : base(message)
    {
    }
}
