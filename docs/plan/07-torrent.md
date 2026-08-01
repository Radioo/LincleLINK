# 07 — Torrent Flow (MonoTorrent)

**Parent:** [00-high-level.md](00-high-level.md) §5, §8, §9 (feature parity)
**Milestone:** M4
**Status:** Approved

## 1. Scope

Port the "Link to torrent" feature from `MainWindowLogic.cs` (CheckFiles / CheckPieces / LinkToTorrent) onto **MonoTorrent** (replaces BencodeNET + hand-rolled `TorrentPiecer`). Also the natural home for the earlier "deferred" torrent data types (`02` §5).

## 2. Layout

| Concern | Type | Where |
|---|---|---|
| Torrent parse port | `ITorrentSource` | `Abstractions/Torrents/ITorrentSource.cs` |
| MonoTorrent adapter | `MonoTorrentSource` | `Infrastructure/Torrents/MonoTorrentSource.cs` |
| Pure torrent data | `TorrentData`, `TorrentFileData` | `Domain/Torrents/` |
| Piece verification | `TorrentPieceVerifier` | `Application/Torrents/TorrentPieceVerifier.cs` |
| Use case | `TorrentService` | `Application/TorrentService.cs` |

## 3. Data model (resolves `02` §5 "deferred")

```csharp
public sealed record TorrentData(
    string Name,
    long TotalSize,
    int PieceLength,
    IReadOnlyList<byte[]> PieceHashes,          // 20-byte SHA1 each (v1 torrents)
    IReadOnlyList<TorrentFileData> Files);

public sealed record TorrentFileData(
    string FullPath,                            // torrent-internal path, '/' separators (BEP)
    long Length);
```

**`MonoTorrentSource`** maps MonoTorrent's parsed model (files with `Path`/`Length`, `PieceLength`, per-piece hashes) into `TorrentData`. Exact load API (`TorrentInfo` vs `Torrent`, piece-hash accessors) is confirmed at M4 implementation from the pinned package.

## 4. `TorrentPieceVerifier` — replaces `TorrentPiecer` (D2)

Ports V2 semantics (byte-for-byte: files in torrent order; matched file → its `db/` bytes; unmatched → **zero bytes**) but **streams with bounded memory** instead of V2's `File.ReadAllBytes` per file and `new byte[file.FileSize]` per missing file (an OOM risk on multi-GB torrents).

```csharp
public sealed class TorrentPieceVerifier
{
    // ctor: TorrentData, canonical-torrent-path → db path map
    public async Task<VerificationResult> VerifyAsync(
        IFileSystem fs, IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed record VerificationResult(
    bool PieceCountMismatch,
    IReadOnlyList<long> BadPieceIndices,
    IReadOnlyList<TorrentFileCheck> Files);

public sealed record TorrentFileCheck(
    string TorrentPath,                         // canonical '/'
    string? HashedFileName,                     // null when file did not match an instance file
    IReadOnlySet<long> Pieces);
```

- Algorithm: walk torrent files in order into a bounded `PieceLength` buffer; fill from the local file's stream for matches, from zero-fill otherwise; hash each completed piece (SHA1); track per-file piece indices; final partial piece zero-padded. Compare computed hashes against `PieceHashes` → bad indices (only when piece counts match, else `PieceCountMismatch`).
- **`IFileSystem` amendment (04):** add `Stream OpenRead(string path)` for streaming hashing.
- Missing files produce all-zero pieces → their pieces land in `BadPieces`, so nothing is ever linked for an unmatched file (V2's guarantee, preserved).

## 5. `TorrentService` — stateless use cases (D3)

Replaces V2's stateful VM fields (`BadPieces`, `FilePieceMap`, `DidCheckFiles`, `DidCheckPieces`) with explicit request/result flow. All user inputs travel in requests; the VM binds to them but holds no operation state.

```csharp
public sealed class TorrentService
{
    // deps: ITorrentSource, IInstanceRepository, IFileStore, IHardLinker,
    //       IFileSystem, IDialogService

    Task<CheckFilesResult> CheckFilesAsync(CheckFilesRequest req,
        IProgress<string>? log, CancellationToken ct = default);

    Task<CheckPiecesResult> CheckPiecesAsync(CheckPiecesRequest req,
        IProgress<string>? log, IProgress<double>? progress, CancellationToken ct = default);

    Task<LinkToTorrentResult> LinkToTorrentAsync(LinkToTorrentRequest req,
        IProgress<string>? log, IProgress<double>? progress, CancellationToken ct = default);
}

public sealed record CheckFilesRequest(string InstanceName, string TorrentPath, string RelativePath);
public sealed record CheckFilesResult(bool Success, string? Error, int Matched, int Total,
    IReadOnlyList<string> MatchedFilePaths);

public sealed record CheckPiecesRequest(string InstanceName, string TorrentPath, string RelativePath);
public sealed record CheckPiecesResult(bool Success, string? Error, bool PieceCountMismatch,
    int MatchedPieces, int TotalPieces, IReadOnlyList<TorrentFileCheck> Files);

public sealed record LinkToTorrentRequest(string DownloadPath, IReadOnlyList<TorrentFileCheck> Files,
    IReadOnlyList<long> BadPieces);
public sealed record LinkToTorrentResult(bool Success, string? Error, int Linked, int Skipped);
```

## 6. Flows (ported)

**CheckFiles** (name + size only):
```
1. data = torrentSource.LoadAsync(torrentPath)                  (error → Error result)
2. instance = repository.GetAsync(instanceName)                 (missing → Error)
3. for each torrentFile:
     relQ = canonicalize(FullPath) minus canonicalize(RelativePath) prefix   [canonical both sides, D4]
     match instance: relPath == relQ && FileName == fileName && FileSize == length
     matched → collect canonical display path
4. return CheckFilesResult(Matched, Total, paths)      (VM enables "Check pieces" when Matched > 0)
```

**CheckPieces** (byte-exact):
```
1. load torrent + instance as above
2. build local map: canonical torrent path → db path, only for exact (path + name + size) matches
3. verifier.VerifyAsync(...) → VerificationResult
4. if PieceCountMismatch → result with flag + V2 log "Piece count does not match..."
   else BadPieceIndices + Files map
5. log V2 lines: "Piece length:", "Number of pieces:", "Beginning piece check..."
6. return CheckPiecesResult(MatchedPieces = TotalPieces − BadCount, ...)
```

**LinkToTorrent** (only fully-verified files):
```
1. validate DownloadPath non-empty                                   (error if empty)
2. for each file in Files:
     if file.Pieces ∩ BadPieces is empty and file.HashedFileName != null:
         target = downloadPath + canonical(file.TorrentPath) → platform separators (IsSafeRelativePath guard)
         create parent dirs
         if !target exists → IHardLinker.TryCreateLink(db/hash, target)   (per-file errors logged, D2)
         Linked++/Skipped++ / failed logged
3. return LinkToTorrentResult
```

## 7. Behavior changes vs V2 (documented)

1. **Streaming, bounded memory** piece verification (D2) — same results, no multi-GB allocations.
2. **Stateless service** (D3) — no cross-call VM state; results passed explicitly. `DidCheckFiles`/`DidCheckPieces` become VM bindings derived from the last result.
3. **Canonicalized path matching** (D4) — instance stored with `\` now matches torrent `\`/`/` paths on both OS (V2 compared raw strings; on Linux a `\`-stored instance vs `/` torrent path could mismatch).
4. **Per-file link failures** logged, not aborting (consistent with `04`/`06`).
5. V1-only torrents supported initially (**D1**); V2/hybrid (BEP 52) torrents rejected with a clear message.

## 8. Edge cases

- Piece-count mismatch → explicit flag + V2 message; no linking.
- Torrent with zero files / empty → error result.
- RelativePath empty → matches whole torrent root (V2 behavior).
- A file whose pieces all match but whose sibling is missing → sibling's pieces bad; sibling not linked; good file links (V2 behavior).
- Cancellation mid-verify → `OperationCanceledException` propagates; nothing saved/linked.

## 9. Test plan (`tests/LincleLINK.Core.Tests/`)

**`Torrents/TorrentPieceVerifierTests`** (pure + TempDir):
- build torrents from known byte layouts; all-match → `BadPieceIndices` empty; one tampered byte → its piece(s) bad; missing file → its pieces bad (zero-hash), other files unaffected;
- piece-count mismatch detected; streaming result **identical to V2 `TorrentPiecer` golden output** for a fixture (regression lock);
- partial-piece zero padding across a file boundary.

**`Torrents/MonoTorrentSourceTests`**: generated `.torrent` fixture → `TorrentData` fields correct (files, piece length, hash list length = `ceil(TotalSize/PieceLength)`).

**`Application/TorrentServiceTests`** (mocked ports):
- check files: matching incl. `\`-vs-`/` canonicalization; RelativePath prefix strip; count + display paths; missing instance/bad torrent → Error;
- check pieces: orchestrates verifier; logs V2 lines; flags mismatch;
- link to torrent: only clean-piece matched files linked; dirs created (normalized); existing targets skipped; per-file failure logged + `Skipped`/error counts; empty download path → Error; `IsSafeRelativePath` violations rejected.

## 10. Decisions (locked)

- **D1** V1 torrents only at first; V2/hybrid (BEP 52) explicitly unsupported with a clear error.
- **D2** Streaming bounded-memory piece verification (fixes V2 full-file/zero-array allocations).
- **D3** `TorrentService` stateless; explicit request/result flow (no VM-held `BadPieces`/`FilePieceMap`/flags).
- **D4** Canonicalized (`/`) path matching for all torrent/instance comparisons.
- **D5** Preserve conservative V2 linking: any file sharing a bad piece is excluded.
