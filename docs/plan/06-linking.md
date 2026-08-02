# 06 — Linking & Maintenance Operations

**Parent:** [00-high-level.md](00-high-level.md) §5, §8, §9 (feature parity)
**Milestone:** M3
**Status:** Approved

## 1. Scope

Define the operations that materialize `db/` data back to disk and keep it tidy, ported from `MainWindowLogic.cs`:

- **Link instance** to a target directory via hard links (`CreateHardLinks`)
- **Copy hashed files** (flat, by hashed name) to a destination (`CopyFiles`)
- **Delete instance** (manifest only — files stay) (`DeleteInstance`)
- **Unused files scan** — find/delete `db/` files referenced by no instance (`CheckForUnusedFiles`)

`LinkToTorrent` is TorrentService (`07`).

## 2. Services

`LinkingService` (link + copy-hashed), `InstanceService` (adds `DeleteInstanceAsync`), `UnusedFilesService` (scan/delete).

```csharp
public sealed class LinkingService
{
    // deps: IFileSystem, IFileStore, IHardLinker, IInstanceRepository,
    //       IDialogService, IProgress logging via params

    public Task<LinkResult> LinkInstanceAsync(string instanceName,
        IProgress<string>? progress = null, CancellationToken ct = default);

    public Task<CopyHashedResult> CopyHashedFilesAsync(string instanceName,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

public sealed record LinkResult(bool Cancelled, int Linked, int Failed, IReadOnlyList<string> Errors);
public sealed record CopyHashedResult(bool Cancelled, int Copied, int AlreadyExisted);
public sealed record DeleteInstanceResult(bool Deleted, bool Cancelled);
public sealed record UnusedFilesResult(int Found, int Deleted, bool Cancelled);
```

**Dialog-driven (D1):** services ask for the target folder and confirmations through `IDialogService` (App adapter), so the ViewModel stays a thin binding shell. `IDialogService` gains the folder-pick member:

```csharp
string? PickFolder(string title);          // null = cancelled
```

## 3. `LinkInstanceAsync` (hard-link materialization)

```
1. target = dialogs.PickFolder("Select link target directory")
       null → return LinkResult(Cancelled: true)
2. instance = repository.GetAsync(instanceName)         (missing → error result)
3. for each dir in instance.DirectoryList:
       normalize (PathNormalizer.ToPlatformSeparators), guard IsSafeRelativePath
       create dir under target (IFileSystem.CreateDirectory)          [v2 order: dirs first]
4. dupes = count target files that already exist
5. if dupes > 0:
       confirm = dialogs.Confirm("N duplicate files exist... 'No' cancels entirely")
       !confirm → return Cancelled
       yes → delete each existing target file (IFileSystem.DeleteFile)
6. for each file in instance.FileList:
       targetPath = normalize(combine(target, relPath, fileName))
       if IHardLinker.TryCreateLink(storePath, targetPath, out err):
           Linked++
       else:
           Failed++, Errors.Add(err)                     [D2: per-file failures log + continue]
       progress
7. return LinkResult
```

- Target paths derived from stored data pass `IsSafeRelativePath` **before** any dir creation or delete (D3).
- V2 parity notes: dirs created before dup check; dup-delete deletes *all* existing target files once confirmed; delete happens only for files in this instance.

## 4. `CopyHashedFilesAsync` (flat copy of hashed files)

```
1. dest = dialogs.PickFolder("Select destination")       null → Cancelled
2. instance = repository.GetAsync(instanceName)
3. for each file:
       destFile = combine(dest, file.HashedFileName)
       if IFileSystem.FileExists(destFile) → AlreadyExisted++  (log "already exists")
       else → CopyFileAsync(storePath, destFile, overwrite: false); Copied++
4. return CopyHashedResult
```

V2 parity: never overwrites; progress per file.

## 5. `DeleteInstanceAsync` (in `InstanceService`, D4)

```
confirm = dialogs.Confirm($"Delete {name}? This will not delete the actual files.")
yes → repository.DeleteAsync(name) → DeleteInstanceResult(Deleted: true)
no  → DeleteInstanceResult(Cancelled: true)
```

- Manifest only; `db/` files untouched. The "Check for unused files" flow then reconciles orphans.

## 6. `UnusedFilesService` — scan & delete orphans

```
1. all = store.GetAllHashedFileNamesAsync()
2. referenced = union of HashedFileName over repository.GetAllAsync()
3. unused = all − referenced
4. none → dialogs.Info("No unused files found.")  → UnusedFilesResult(0,0,false)
5. found → confirm = dialogs.Confirm($"N unused files found. Delete?")
       yes → store.DeleteAsync each; Deleted++
       no  → UnusedFilesResult(found, 0, true)
```

- Progress not needed (fast enumeration); results drive the UI status refresh.

## 7. Behavior changes vs V2 (documented)

1. **Per-file hard-link failures no longer abort the whole operation** (D2). V2 threw inside one big try/catch, killing the link loop on the first failure and leaving the error as a single log line. v3 logs each failure, continues, and reports `Failed`/`Errors` at the end. (Consistent with `04` D2.)
2. **All paths derived from stored data are validated** (`IsSafeRelativePath`) before use (D3) — V2 wrote whatever was in the JSON.
3. Delete-instance and unused-files keep V2 semantics exactly.

## 8. Edge cases

- **Cross-device target** (EXDEV): `IHardLinker` returns a clear per-file error → logged, op continues (V2 aborted). The UI result surfaces the count.
- **Target dir nested inside `db/`**: no special handling (V2 had none); possible but unusual — note in docs, not prevented.
- **Missing instance file** between selection and load: `GetAsync` → null → error result (no crash).
- **Move-mode orphans** from a cancelled add (`05`) are naturally found by the unused-files scan.

## 9. Test plan (`tests/LincleLINK.Core.Tests/Application/`)

**`LinkingServiceTests`** (mocked ports):
- folder pick cancelled → `LinkResult(Cancelled)`; no dirs created, no links;
- dirs created with normalized platform-separator paths; `IsSafeRelativePath` violation → no dir created, error recorded;
- dup detection: 0 dupes → straight to linking; dupes + confirm-No → Cancelled (no deletes, no links); dupes + confirm-Yes → existing files deleted then linked;
- per-file `TryCreateLink` failure → logged, `Failed`/`Errors` incremented, loop continues;
- copy-hashed: existing dest skipped (`AlreadyExisted`), new dest copied without overwrite; folder pick cancelled → Cancelled.

**`InstanceServiceTests`** (extended): delete confirm-No → repo untouched; confirm-Yes → `DeleteAsync` called once.

**`UnusedFilesServiceTests`**: no unreferenced files → Info, nothing deleted; found + confirm → deleted count correct; found + confirm-No → Cancelled, nothing deleted.

**Integration (real `TempDir`)**: build a small `db/` + instance, link to a target → files exist and share the same inode (platform-conditional), copy-hashed flat, unused scan reconciles orphans.

## 10. Decisions (locked)

- **D1** Services drive folder-pick + confirmations through `IDialogService` (VMs stay thin).
- **D2** Per-file hard-link failures log and continue; result reports `Failed`/`Errors`.
- **D3** All stored-data-derived target paths validated with `PathNormalizer.IsSafeRelativePath`.
- **D4** `DeleteInstanceAsync` lives in `InstanceService` (instance lifecycle), not `LinkingService`.
