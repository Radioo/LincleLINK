# 05 — Instance Creation Flow (Add Instance)

**Parent:** [00-high-level.md](00-high-level.md) §5, §8, §9 (feature parity)
**Milestone:** M2
**Status:** Approved

## 1. Scope

Define the `InstanceService` use case that mirrors V2's "Add instance" dialog: name + data-path input, validation (incl. low-disk warning), hashing, dedup copy/move into `db/`, directory collection, and instance save. Behavior ported from `AddInstanceWindowLogic.cs` with fixes noted inline.

## 2. The service

```csharp
public sealed class InstanceService
{
    // deps: IFileSystem, IFileHasher, IFileStore, IInstanceRepository,
    //       IDriveInfoProvider, IAppPaths, IDialogService, ILogger/LogSink

    public Task<AddInstanceResult> CreateInstanceAsync(
        AddInstanceRequest request, IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public sealed record AddInstanceRequest(
    string InstanceName,
    string DataPath,
    CopyMoveMode Mode,
    int? MaxDegreeOfParallelism = null);

public sealed record AddInstanceResult(
    bool Success,
    string? Error,                 // user-presentable failure message
    int FilesAdded,                // new files written to db
    long BytesAdded,
    int AlreadyExisted,
    int TotalFiles);
```

- **UI-free:** all confirmation/error dialogs go through `IDialogService` (App adapter). `AddInstanceViewModel` is a thin binding shell (details in `08`).
- **Progress:** `IProgress<string>` carries log lines (hashing each file, "collecting directories", "saving…") — the V2 log panel behavior, but via `IProgress` instead of `ObservableCollection` mutation from logic. Numeric progress (`double`) is derived by the VM from `TotalFiles`/`FilesAdded` (or a second `IProgress<double>`; see D1).
- **Parallel hashing (D5):** Phase A hashes files in parallel, bounded by `AddInstanceRequest.MaxDegreeOfParallelism` (default `Environment.ProcessorCount`, clamped to `1..ProcessorCount`). The value comes from the Other-tab thread slider via settings (`03`).
- **Off-thread I/O (D5):** file enumeration (+ the copy-mode size precompute, which is one metadata round-trip per file) and Phases A+B (hash / dedup-copy / save) run on the thread pool via `Task.Run`, so no add-instance file I/O executes on the UI thread; validation and the low-disk dialog stay on the caller thread.
- **Log flood control (D6):** per-file `IProgress<string>` lines are delivered to the UI log in bounded batches at Background dispatcher priority (`BatchedLog`, App), and the log panel is a virtualizing `ListBox` that keeps every line while only realizing the visible ones (`08`).

## 3. Flow (port of V2, with fixes)

```
Validate(name, dataPath, mode)          ── validate name (InstanceNameValidator)
                                          validate data path exists + is a directory
                                          check space (only when mode == Copy)
    │   fail → return AddInstanceResult(false, message)
    ▼
Enumerate files (recursive)            ── IFileSystem.EnumerateFiles(dataPath, recursive)
   build relative path via PathNormalizer (V2: Path.GetRelativePath, '.' → '')
    │
    ▼
For each file (ct cancellable, progress per file):
    hash = IFileHasher.ComputeHashAsync(file)         (V2: MD5)
    storeName = hash + Path.GetExtension(fileName)
    if mode == Copy:  IFileStore.CopyToStoreAsync(source, storeName)   // dedups
    if mode == Move:  IFileStore.MoveToStoreAsync(source, storeName)   // dedups
    count new vs existing (BytesAdded when newly added)
    record InstanceFile(fileName, relPath, length, storeName)
    │
    ▼
Enumerate directories (recursive)      ── V2 parity: derived from Directory.GetDirectories
    relative dirs
    ▼
Instance.Create(name, files, dirs)
IInstanceRepository.SaveAsync(instance)  (recomputes TotalFileSizeString, atomic write)
    ▼
return AddInstanceResult(...)
```

## 4. Validation rules (V2 → v3)

| Check | V2 behavior | v3 |
|---|---|---|
| Empty/whitespace name | message "Instance name cannot be empty" | `InstanceNameValidator` → message |
| Duplicate name (case-insensitive) | enumerated `instance/*.json`, `.ToLower()` compare | `IInstanceRepository.ExistsAsync` (OrdinalIgnoreCase, `03`) |
| Invalid name chars | `Path.GetInvalidFileNameChars()` (platform-dependent) | `InstanceNameValidator` (platform-stable set, `02`) |
| Empty/invalid data path | "Data path cannot be empty" | path exists + `Directory.Exists` |
| Data path contains invalid chars | `Path.GetInvalidFileNameChars` per char | path existence covers it; drop char check (D2) |
| Low disk space (copy mode only) | compute size, +100MB wiggle room, Yes/No warning | `IDriveInfoProvider.GetAvailableFreeSpace(dataRoot)`; same +100MB threshold; warning via `IDialogService` |

**V2 low-disk quirk (D2):** V2 computed `size_to_copy` by enumerating the *data path* files, and `free_space` on the **db drive** (`currentDir`). v3 computes free space on the **data path's volume** (the volume files actually land on after copy). This is a correctness fix — flag in changelog.

## 5. Behavior changes vs V2 (documented, deliberate)

1. **Move-mode semantics (D3, superseded):** V2 left a dedup-hit source file in place (`File.Move` skipped by the `if (!File.Exists(...))` guard). The shipped v3 behavior differs deliberately: in move mode **every** original — new hash or dedup hit — is replaced with a hard link to its `db/` copy, so the source tree ends fully deduplicated. See `12-verification.md` §6 for the change register entry and `14-ux-clarity.md` D3 for the link-then-replace ordering.
2. **No files in data path:** V2 would create an empty instance (0 files) and save it. v3 rejects with a user-facing error (D4) — an instance with zero files is useless and confuses the "unused files" scan.
3. **Progress reporting** decoupled from UI collection (D1).
4. Low-disk volume fix (above).

## 6. Edge cases

- **Cancellation mid-add:** `ct` checked per file. On cancel: files already moved to `db/` stay (orphans are reconciled by the unused-files scan); the instance is **not** saved. Partial state is acceptable and recoverable.
- **Move mode + crash:** `File.Move` is same-volume atomic; a crash leaves either source or dest present, never both.
- **Duplicate file names differing only by case** in the data path: two `InstanceFile` records with different `FileName` but (potentially) equal `HashedFileName` if content matches; `db/` dedup handles it; instance list keeps both entries (V2 parity).
- **Huge instance:** memory — `FileList` holds one record per file (V2 parity; acceptable for this domain, note for `10-testing` to cap fixture sizes).

## 7. Return vs UI

`AddInstanceResult` drives the dialog's closing log lines exactly as V2:
- `"Instance added. {AlreadyExisted} files already exist. {ReadableSize(BytesAdded)} added to the db."`

## 8. Test plan (`tests/LincleLINK.Core.Tests/Application/InstanceServiceTests.cs`)

Mocked ports (`IFileSystem`, `IFileHasher`, `IDriveInfoProvider`, `IFileStore`, `IInstanceRepository`, `IDialogService`):

- name validation failures (empty, duplicate, invalid chars) → `Success=false` + message, no IO;
- data path missing → failure;
- copy mode: new files copied + counted `FilesAdded`/`BytesAdded`; existing deduped → `AlreadyExisted`, store skip;
- move mode: source removed, dedup leaves existing source in place (V2 parity);
- low-disk: free space below threshold + user confirms → proceeds; user declines → aborts; mode Copy only;
- directories collected as relative paths (`.` → `""`);
- instance saved with recomputed totals; `SaveAsync` called exactly once on success, never on failure/cancel;
- cancel mid-loop → no save, partial `db/` files remain;
- empty data path → error (D4);
- progress callback receives expected log lines.

## 9. Decisions (locked)

- **D1** Progress via `IProgress<string>` for logs (VM derives numeric progress).
- **D2** Drop the V2 per-char data-path validation; rely on path existence. Low-disk free space measured on the **data path volume** (fix).
- **D3** *(superseded in implementation — see §5.1 and `14-ux-clarity.md`)* Move mode replaces every original with a hard link to its `db/` copy; the file is counted `AlreadyExisted` when its hash was already stored.
- **D4** Reject instances with zero files (error) instead of saving an empty instance.
- **D5** Parallel hashing bounded by `MaxDegreeOfParallelism` from the Other-tab thread slider; all file I/O (enumeration, sizing, phases) off the UI thread.
- **D6** Add-instance log lines batched to the UI at Background priority and rendered in a virtualizing (keep-all-lines) `ListBox`.
