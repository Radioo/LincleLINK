# 02 — Domain Models

**Parent:** [00-high-level.md](00-high-level.md) §5 (Architecture), §6 (Cross-platform), §8 (Testing)
**Milestone:** M1
**Status:** Approved

## 1. Scope

Define the pure domain types in `LincleLINK.Core/Domain/`: the data model that persists as `instance/<Name>.json`, its value types, size formatting, and instance-name validation. Hard constraint: **v3 must read every existing v2 instance file unchanged and write byte-identical JSON schema.**

## 2. JSON compatibility contract (with v2)

v2 serializes with System.Text.Json **default naming (PascalCase)** + `WriteIndented = true`. Round-trip fidelity for `instance/<Name>.json` is the #1 requirement. Tests must lock this with a fixture captured from a real v2 run.

v2 `InstanceFile` (declaration order = JSON order):

```json
{
  "FileName": "25063_pre.2dx",
  "RelativePath": "sound\\25063",
  "FileSize": 463806,
  "HashedFileName": "7AFE6AC1B80128D44BA5357D4349B21A.2dx"
}
```

v2 `Instance` (declaration order = JSON order):

```json
{
  "Name": "...",
  "TotalFileSize": 123456,
  "TotalFileCount": 10,
  "TotalFileSizeString": "...",
  "FileList": [ ... ],
  "DirectoryList": [ "sound\\25063", ... ]
}
```

Rules for the new model:

1. Property names and **declaration order** match v2 exactly (STJ emits in declaration order) → output stays diff-friendly.
2. `TotalFileSize` / `TotalFileCount` / `TotalFileSizeString` are **persisted denormalized fields** (kept for compat). `TotalFileSizeString` is recomputed at save time so stored strings stay consistent (D3).
3. `RelativePath` and `DirectoryList` keep v2 backslash strings on disk. The domain does **not** normalize paths on load; normalization happens at IO time in services (`04-filesystem-ports.md`). Value in memory is the stored string.
4. Missing/empty collections deserialize to empty lists (never null) so consumers can iterate safely.

## 3. Types

### 3.1 `InstanceFile` (`record`)

```csharp
namespace LincleLINK.Core.Domain;

public sealed record InstanceFile(
    string FileName,
    string RelativePath,
    long FileSize,
    string HashedFileName);
```

- `record` → value equality (used by torrent file matching and tests).
- `FileSize` invariant: `>= 0`. Invalid sizes are a data/validation concern of `InstanceService`, not the type (kept as plain data; no throwing guards — see §6).

### 3.2 `Instance` (`sealed class`)

```csharp
public sealed class Instance
{
    public string Name { get; set; }                 // required, filesystem-safe (see §5)
    public long TotalFileSize { get; set; }
    public int TotalFileCount { get; set; }
    public string TotalFileSizeString { get; set; }  // denormalized, recomputed on save
    public List<InstanceFile> FileList { get; set; }
    public List<string> DirectoryList { get; set; }

    public static Instance Create(string name, IEnumerable<InstanceFile> files,
        IEnumerable<string> directories);           // computes totals + unique dir list
}
```

- `class` (not record): keeps STJ round-trip dead simple and identical to v2 output; instances are mutable aggregates, not values.
- `Create` mirrors the v2 constructor: sets `FileList`/`DirectoryList` (as lists), `TotalFileSize` = sum of `FileSize`, `TotalFileCount` = count, `TotalFileSizeString` = `SizeFormatter.Format(...)`.
- No nulls: `Create` materializes enumerables; deserialization normalizes null collections to empty (via STJ options in the repository, `03`).

### 3.3 `InstanceListEntry` (`record`)

```csharp
public sealed record InstanceListEntry(
    string InstanceName,
    int FileCount,
    long TotalFileSize,
    string TotalFileSizeString)
{
    public static InstanceListEntry From(Instance instance);
}
```

Lightweight projection for list views (DataGrid row). Kept in Domain because it is derived, pure data used by both UI and summaries.

### 3.4 `SizeFormatter` (static)

Port of v2 `Instance.ReadableSize` with the **boundary bug fixed** (D2). v2 thresholds used strict `>`/`<`, so exact powers of 1024 (e.g. 1024 B) fell through to the TB branch and rendered as `~0 TB`. Corrected logic:

```csharp
public static class SizeFormatter
{
    public static string Format(long size)   // size < 0 → throw ArgumentOutOfRangeException
    {
        // < 1 KiB        → "{size} B"
        // < 1 MiB        → "{n} KB"   (2 decimals)
        // < 1 GiB        → "{n} MB"
        // < 1 TiB        → "{n} GB"
        // else           → "{n} TB"
    }
}
```

- Units B / KB / MB / GB / TB, two decimal places, identical style to v2.
- Difference vs v2 only at exact `1024^n` boundaries; cosmetic and strictly more correct.

### 3.5 `InstanceNameValidator` (static) — cross-platform safe

v2 used `Path.GetInvalidFileNameChars()` (platform-dependent) plus a case-insensitive uniqueness check against existing instance files. v3 must validate a **platform-stable superset** so a `db`/`instance` folder can move between Windows and Linux:

1. Empty / whitespace-only → invalid.
2. Contains any char invalid on **Windows** (`Path.GetInvalidFileNameChars()` on Windows) **or** `/` or `\` (both separators, so names don't collide with paths).
3. Contains `:` `<` `>` `"` `|` `?` `*` (Windows reserved) — covered by the Windows set.
4. Trailing dot or space, or equals a Windows reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`, case-insensitive) — avoids Windows-illegal folder names for portability.
5. Uniqueness (case-insensitive on all platforms) is enforced by the repository/service layer, not the validator — it needs IO.

```csharp
public static class InstanceNameValidator
{
    public static bool IsValid(string name);        // structural check only (rules 1-4)
    public static string? FirstError(string name);  // null if valid, else human-readable reason
}
```

Rules 2–4 hard-coded as the union set (not `Path.GetInvalidFileNameChars()` at runtime) so results are identical on both OS.

## 4. Copy/move mode

```csharp
public enum CopyMoveMode { Copy, Move }
```

Used by `InstanceService` (add-instance flow) and surfaced by `AddInstanceViewModel`. Domain-only enum; JSON never stores it.

## 5. What is NOT in Domain (deferred / elsewhere)

- Torrent result types (`PieceCheckReport`, matched-file lists) → defined in `07-torrent.md`; plain data, may live in Domain or Application.
- JSON serialization (`JsonInstanceSerializer` / options) → Infrastructure, `03-storage-repository.md`.
- Path normalization → `04-filesystem-ports.md`.
- Hashing (`IFileHasher`) → `04`.
- Legacy v1 XML types (`DBInfo`, `DataInstance`, `InstanceFileInfo`) stay internal to `LegacyImporter` (Application) — they are import-only DTOs, not domain.

## 6. Validation philosophy

Domain types are plain data carriers. Invariants are enforced at the service boundary (`InstanceService` validates instance names, re-checks sizes, rejects empty file lists with a user-facing error via `IDialogService`). Throwing in domain constructors is avoided except for programming errors (`SizeFormatter` on negative size). Rationale: the persisted JSON is loaded from disk and must never fail to deserialize into the model, even if hand-edited or from a future version.

## 7. Test plan (mapped to `tests/LincleLINK.Core.Tests/Domain/`)

**`SizeFormatterTests`**
- 0 B, 1 B, 1023 B → `B`; 1024 B → `1 KB` (v2-bug regression test); 1 MiB / 1 GiB / 1 TiB boundaries; fractional rounding (e.g. 1536 → `1.5 KB`); large values → TB; negative → throws.

**`InstanceNameValidatorTests`**
- valid names; empty/whitespace; Windows-invalid chars; both separators; trailing dot/space; reserved device names (case-insensitive); names valid on Linux but invalid on Windows (must be rejected for portability).

**`Instance` round-trip / fixture tests** (fixtures in `TestHelpers/TestData.cs`)
- Deserialize a v2-captured JSON fixture → fields match; serialize → JSON is *equal to the fixture* (byte-level, ignoring line endings).
- `Create` computes totals correctly; null/missing collections in JSON → empty lists after load.

## 8. Decisions (locked)

- **D1** `InstanceFile` = `record`, `Instance` = `sealed class` (mutable aggregate; STJ-compat).
- **D2** Fix `ReadableSize` boundary bug (exact `1024^n` now renders correctly; cosmetic change).
- **D3** Recompute `TotalFileSizeString` on save (normalizes existing files; derived data).
- **D4** `InstanceListEntry` stays in Domain as a pure projection.
- **D5** Validator rejects Windows-illegal names on all platforms (portability).
