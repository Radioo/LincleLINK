using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LincleLINK.Core.Application;

public sealed record TorrentCheckRequest(string InstanceName, string TorrentPath, string RelativePath);
public sealed record CheckFilesResult(bool Success, string? Error, int Matched, int Total, IReadOnlyList<string> MatchedFilePaths);

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
public sealed partial class TorrentService
{
    private readonly ITorrentSource _torrentSource;
    private readonly IInstanceRepository _repository;
    private readonly IFileStore _store;
    private readonly IHardLinker _hardLinker;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<TorrentService> _logger;

    public TorrentService(
        ITorrentSource torrentSource,
        IInstanceRepository repository,
        IFileStore store,
        IHardLinker hardLinker,
        IFileSystem fileSystem,
        ILogger<TorrentService> logger)
    {
        _torrentSource = torrentSource;
        _repository = repository;
        _store = store;
        _hardLinker = hardLinker;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<CheckFilesResult> CheckFilesAsync(
        TorrentCheckRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var (torrent, error) = await LoadAsync(request.TorrentPath, ct);
        if (torrent is null)
        {
            return new CheckFilesResult(false, error, 0, 0, []);
        }

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, request.InstanceName, ct);
        if (instance is null)
        {
            return new CheckFilesResult(false, notFound, 0, 0, []);
        }

        var relativePrefix = PathNormalizer.Canonicalize(request.RelativePath);
        var matched = new List<string>();
        var progress = ProgressStep.Over(torrent.Files.Count);
        int fileIndex = 0;

        foreach (var file in torrent.Files)
        {
            ct.ThrowIfCancellationRequested();

            var full = PathNormalizer.Canonicalize(file.FullPath);
            if (!PathNormalizer.IsWithin(full, relativePrefix))
            {
                percent?.Report(progress.Report(ref fileIndex));
                continue;
            }

            var relQ = relativePrefix.Length == 0 ? full : full[relativePrefix.Length..].TrimStart('/');

            var hit = MatchInstanceFile(file, relQ, instance);

            if (hit is not null)
            {
                matched.Add(relQ);
            }

            percent?.Report(progress.Report(ref fileIndex));
        }

        log?.Report($"Matched {matched.Count} out of {torrent.Files.Count} files (compared names and sizes).");
        LogFilesMatched(request.InstanceName, matched.Count, torrent.Files.Count);
        return new CheckFilesResult(true, null, matched.Count, torrent.Files.Count, matched);
    }

    public async Task<CheckPiecesResult> CheckPiecesAsync(
        TorrentCheckRequest request,
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        var (torrent, error) = await LoadAsync(request.TorrentPath, ct);
        if (torrent is null)
        {
            return new CheckPiecesResult(false, error, false, 0, 0, [], []);
        }

        var (instance, notFound) = await InstanceLookup.GetAsync(_repository, request.InstanceName, ct);
        if (instance is null)
        {
            return new CheckPiecesResult(false, notFound, false, 0, 0, [], []);
        }

        log?.Report($"Piece length: {torrent.PieceLength}");
        log?.Report($"Number of pieces: {torrent.PieceHashes.Count}");
        log?.Report("Beginning piece check, this might take a while...");
        LogPieceCheckStarted(request.InstanceName, torrent.PieceHashes.Count, torrent.PieceLength);

        var relativePrefix = PathNormalizer.Canonicalize(request.RelativePath);
        var localFiles = BuildLocalFileMap(torrent, instance, relativePrefix);

        var verifier = new TorrentPieceVerifier(torrent, localFiles);
        var result = await verifier.VerifyAsync(_fileSystem, percent, ct);

        if (result.PieceCountMismatch)
        {
            log?.Report("Piece count does not match, something went terribly wrong.");
            LogPieceCountMismatch(request.InstanceName);
            return new CheckPiecesResult(false, "Piece count does not match.", true, 0, torrent.PieceHashes.Count, [], []);
        }

        var total = torrent.PieceHashes.Count;
        var matched = total - result.BadPieceIndices.Count;
        log?.Report($"Piece check finished. {matched} out of {total} pieces matched.");
        LogPieceCheckCompleted(request.InstanceName, matched, total);

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

        log?.Report("Linking verified files...");

        var progress = ProgressStep.Over(request.Files.Count);
        int index = 0;

        foreach (var file in request.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (file.HashedFileName is null || file.Pieces.Any(bad.Contains))
            {
                skipped++;
                continue;
            }

            // Validate before Path.Combine: a rooted/'..' TorrentPath must not be
            // turned into a path outside DownloadPath.
            if (!PathNormalizer.IsSafeRelativePath(file.TorrentPath))
            {
                LogSkippedUnsafePath(file.TorrentPath);
                log?.Report($"Skipped unsafe path '{file.TorrentPath}'.");
                skipped++;
                continue;
            }

            var target = PathNormalizer.ToPlatformSeparators(Path.Combine(request.DownloadPath, file.TorrentPath));

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
                LogFileLinkedToTorrent(file.HashedFileName, target);
                linked++;
            }
            else
            {
                LogTorrentLinkFailed(file.TorrentPath, linkError ?? "unknown error");
                log?.Report($"{file.TorrentPath}: {linkError}");
                skipped++;
            }

            percent?.Report(progress.Report(ref index));
        }

        log?.Report("Linking finished");
        LogTorrentLinkingCompleted(linked, skipped);
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
            if (!PathNormalizer.IsWithin(full, relativePrefix))
            {
                continue;
            }

            var relQ = relativePrefix.Length == 0 ? full : full[relativePrefix.Length..].TrimStart('/');

            var hit = MatchInstanceFile(file, relQ, instance);

            if (hit is not null)
            {
                map[full] = _store.GetPath(hit.HashedFileName);
            }
        }

        return map;
    }

    /// <summary>
    /// Matches a torrent file to the instance file with the same canonical relative
    /// path and size. Shared by <see cref="CheckFilesAsync"/> and
    /// <see cref="BuildLocalFileMap"/> so matching semantics stay in one place.
    /// </summary>
    private static InstanceFile? MatchInstanceFile(
        Domain.Torrents.TorrentFileData file,
        string relQ,
        Instance instance)
        => instance.FileList.FirstOrDefault(f =>
            string.Equals(PathNormalizer.Canonicalize(f.RelativePath + "/" + f.FileName), relQ, StringComparison.Ordinal)
            && f.FileSize == file.Length);

    private async Task<(Domain.Torrents.TorrentData? Torrent, string? Error)> LoadAsync(string torrentPath, CancellationToken ct)
    {
        try
        {
            var torrent = await _torrentSource.LoadAsync(torrentPath, ct);
            return (torrent, null);
        }
        catch (TorrentNotSupportedException ex)
        {
            // Unsupported torrent is a user-presentable condition; other failures
            // (IO, permission, bugs) propagate to the VM boundary like every service.
            return (null, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Torrent file check for '{InstanceName}': matched {Matched} of {Total} files")]
    private partial void LogFilesMatched(string instanceName, int matched, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Piece check for '{InstanceName}' started: {TotalPieces} pieces of {PieceLength} bytes")]
    private partial void LogPieceCheckStarted(string instanceName, int totalPieces, long pieceLength);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Piece count mismatch for '{InstanceName}'")]
    private partial void LogPieceCountMismatch(string instanceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Piece check for '{InstanceName}' completed: {Matched} of {Total} pieces matched")]
    private partial void LogPieceCheckCompleted(string instanceName, int matched, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped unsafe torrent path '{TorrentPath}'")]
    private partial void LogSkippedUnsafePath(string torrentPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Linked {StoreName} to {Target}")]
    private partial void LogFileLinkedToTorrent(string storeName, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to link torrent file '{TorrentPath}': {Error}")]
    private partial void LogTorrentLinkFailed(string torrentPath, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Torrent linking completed: {Linked} linked, {Skipped} skipped")]
    private partial void LogTorrentLinkingCompleted(int linked, int skipped);
}
