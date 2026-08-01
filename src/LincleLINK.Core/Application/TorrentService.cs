using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record CheckFilesRequest(string InstanceName, string TorrentPath, string RelativePath);
public sealed record CheckFilesResult(bool Success, string? Error, int Matched, int Total, IReadOnlyList<string> MatchedFilePaths);

public sealed record CheckPiecesRequest(string InstanceName, string TorrentPath, string RelativePath);
public sealed record CheckPiecesResult(
    bool Success,
    string? Error,
    bool PieceCountMismatch,
    int MatchedPieces,
    int TotalPieces,
    IReadOnlyList<long> BadPieces,
    IReadOnlyList<TorrentFileCheck> Files);

public sealed record LinkToTorrentRequest(string DownloadPath, IReadOnlyList<TorrentFileCheck> Files, IReadOnlyList<long> BadPieces);
public sealed record LinkToTorrentResult(bool Success, string? Error, int Linked, int Skipped);

/// <summary>
/// Torrent-aware linking (plan 07). Stateless: all inputs travel in requests and all
/// results come back explicitly. Only files whose pieces all match are linked.
/// </summary>
public sealed class TorrentService
{
    private readonly ITorrentSource _torrentSource;
    private readonly IInstanceRepository _repository;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IFileSystem _fileSystem;

    public TorrentService(
        ITorrentSource torrentSource,
        IInstanceRepository repository,
        IFileStore store,
        IHardLinker hardLinker,
        IFileSystem fileSystem)
    {
        _torrentSource = torrentSource;
        _repository = repository;
        _store = store;
        _hardLinker = hardLinker;
        _fileSystem = fileSystem;
    }

    public async Task<CheckFilesResult> CheckFilesAsync(
        CheckFilesRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var (torrent, error) = await LoadAsync(request.TorrentPath, ct);
        if (torrent is null)
        {
            return new CheckFilesResult(false, error, 0, 0, []);
        }

        var instance = await _repository.GetAsync(request.InstanceName, ct);
        if (instance is null)
        {
            return new CheckFilesResult(false, $"Instance '{request.InstanceName}' not found.", 0, 0, []);
        }

        var relativePrefix = PathNormalizer.Canonicalize(request.RelativePath);
        var matched = new List<string>();
        double step = torrent.Files.Count == 0 ? 0 : 100d / torrent.Files.Count;
        int fileIndex = 0;

        foreach (var file in torrent.Files)
        {
            ct.ThrowIfCancellationRequested();

            var full = PathNormalizer.Canonicalize(file.FullPath);
            if (!full.StartsWith(relativePrefix, StringComparison.Ordinal))
            {
                percent?.Report(++fileIndex * step);
                continue;
            }

            var relQ = relativePrefix.Length == 0 ? full : full[relativePrefix.Length..].TrimStart('/');

            var hit = instance.FileList.FirstOrDefault(f =>
                string.Equals(PathNormalizer.Canonicalize(f.RelativePath + "/" + f.FileName), relQ, StringComparison.Ordinal)
                && f.FileSize == file.Length);

            if (hit is not null)
            {
                matched.Add(relQ);
            }

            percent?.Report(++fileIndex * step);
        }

        log?.Report($"Matched {matched.Count} out of {torrent.Files.Count} files (compared names and sizes).");
        return new CheckFilesResult(true, null, matched.Count, torrent.Files.Count, matched);
    }

    public async Task<CheckPiecesResult> CheckPiecesAsync(
        CheckPiecesRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var (torrent, error) = await LoadAsync(request.TorrentPath, ct);
        if (torrent is null)
        {
            return new CheckPiecesResult(false, error, false, 0, 0, [], []);
        }

        var instance = await _repository.GetAsync(request.InstanceName, ct);
        if (instance is null)
        {
            return new CheckPiecesResult(false, $"Instance '{request.InstanceName}' not found.", false, 0, 0, [], []);
        }

        log?.Report($"Piece length: {torrent.PieceLength}");
        log?.Report($"Number of pieces: {torrent.PieceHashes.Count}");
        log?.Report("Beginning piece check, this might take a while...");

        var relativePrefix = PathNormalizer.Canonicalize(request.RelativePath);
        var localFiles = BuildLocalFileMap(torrent, instance, relativePrefix);

        var verifier = new TorrentPieceVerifier(torrent, localFiles);
        var result = await verifier.VerifyAsync(_fileSystem, percent, ct);

        if (result.PieceCountMismatch)
        {
            log?.Report("Piece count does not match, something went terribly wrong.");
            return new CheckPiecesResult(false, "Piece count does not match.", true, 0, torrent.PieceHashes.Count, [], []);
        }

        var total = torrent.PieceHashes.Count;
        var matched = total - result.BadPieceIndices.Count;
        log?.Report($"Piece check finished. {matched} out of {total} pieces matched.");

        return new CheckPiecesResult(true, null, false, matched, total, result.BadPieceIndices, result.Files);
    }

    public async Task<LinkToTorrentResult> LinkToTorrentAsync(
        LinkToTorrentRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DownloadPath))
        {
            return new LinkToTorrentResult(false, "Torrent download path is empty.", 0, 0);
        }

        var bad = new HashSet<long>(request.BadPieces);
        int linked = 0;
        int skipped = 0;

        log?.Report("Linking...");

        double step = request.Files.Count == 0 ? 0 : 100d / request.Files.Count;
        int index = 0;

        foreach (var file in request.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (file.HashedFileName is null || file.Pieces.Any(bad.Contains))
            {
                skipped++;
                continue;
            }

            var target = PathNormalizer.ToPlatformSeparators(Path.Combine(request.DownloadPath, file.TorrentPath));
            if (!PathNormalizer.IsSafeRelativePath(file.TorrentPath))
            {
                log?.Report($"Skipped unsafe path '{file.TorrentPath}'.");
                skipped++;
                continue;
            }

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                _fileSystem.CreateDirectory(dir);
            }

            if (_fileSystem.FileExists(target))
            {
                skipped++;
            }
            else if (_hardLinker.TryCreateLink(_store.GetPath(file.HashedFileName), target, out var linkError))
            {
                linked++;
            }
            else
            {
                log?.Report($"{file.TorrentPath}: {linkError}");
                skipped++;
            }

            percent?.Report(++index * step);
        }

        log?.Report("Linking finished");
        return new LinkToTorrentResult(true, null, linked, skipped);
    }

    private Dictionary<string, string> BuildLocalFileMap(
        Domain.Torrents.TorrentData torrent,
        Instance instance,
        string relativePrefix)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in torrent.Files)
        {
            var full = PathNormalizer.Canonicalize(file.FullPath);
            if (!full.StartsWith(relativePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relQ = relativePrefix.Length == 0 ? full : full[relativePrefix.Length..].TrimStart('/');

            var hit = instance.FileList.FirstOrDefault(f =>
                string.Equals(PathNormalizer.Canonicalize(f.RelativePath + "/" + f.FileName), relQ, StringComparison.Ordinal)
                && f.FileSize == file.Length);

            if (hit is not null)
            {
                map[full] = _store.GetPath(hit.HashedFileName);
            }
        }

        return map;
    }

    private async Task<(Domain.Torrents.TorrentData? Torrent, string? Error)> LoadAsync(string torrentPath, CancellationToken ct)
    {
        try
        {
            var torrent = await _torrentSource.LoadAsync(torrentPath, ct);
            return (torrent, null);
        }
        catch (TorrentNotSupportedException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            return (null, $"Could not load torrent file: {ex.Message}");
        }
    }
}
