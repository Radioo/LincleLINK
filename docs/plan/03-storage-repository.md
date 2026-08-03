# 03 - Storage & Repository

**Parent:** [00-high-level.md](00-high-level.md) §5, §6, §8
**Milestone:** M1
**Status:** Approved

**Revision history:** rev 2 approved - settings split from data dir; `DataDirectory` stored in settings; first-launch V2 migration.

## 1. Scope

Persistence layer in `LincleLINK.Core`: ports (`IAppPaths`, `IAppPathsFactory`, `IInstanceRepository`, `IFileStore`, `ISettingsStore`), implementations, the v2-compatible JSON serializer, legacy V2 data adoption, and the v1 XML importer. Disk layout for **user data** stays exactly as V2: CWD-derived `db/` + `instance/<Name>.json`. **App settings** move to per-OS config.

## 2. Layout summary

| Concern | Port | Impl |
|---|---|---|
| Data-root → `db/`, `instance/` | `Abstractions/Paths/IAppPaths.cs` (+ `IAppPathsFactory`) | `Infrastructure/Paths/AppPaths.cs` |
| App settings (incl. data dir) | `Abstractions/Settings/ISettingsStore.cs` | `Infrastructure/Settings/JsonSettingsStore.cs` |
| Instance CRUD (JSON) | `Abstractions/Instances/IInstanceRepository.cs` | `Infrastructure/Instances/JsonInstanceRepository.cs` |
| Dedup store (`db/`) | `Abstractions/Storage/IFileStore.cs` | `Infrastructure/Storage/FileStore.cs` |
| v2-compatible JSON options | - | `Infrastructure/Serialization/InstanceJson.cs` |
| First-launch + V2 adoption | - | `Application/FirstLaunchService.cs` |
| Legacy v1 XML import | - | `Application/LegacyImporter.cs` |

## 3. Settings - split from data dir

```csharp
public sealed record AppSettings(bool IsDarkTheme, string? DataDirectory, int HashThreadCount);

public interface ISettingsStore
{
    bool Exists { get; }            // first-launch detection
    AppSettings Load();
    void Save(AppSettings settings);
}
```

- **Location (D1):** `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LincleLINK", "settings.json")` → `%APPDATA%\LincleLINK\settings.json` on Windows, `~/.config/LincleLINK/settings.json` on Linux, `~/Library/Application Support/LincleLINK/settings.json` on macOS. Never inside the data dir.
- Schema: `{ "IsDarkTheme": bool, "DataDirectory": string | null, "HashThreadCount": int }`. Key `IsDarkTheme` matches actual v2 code (AGENTS.md's `{"IsDark": bool}` is inaccurate).
- `DataDirectory = null` → default (current working directory).
- `HashThreadCount` bounds the parallel hashing workers used by add-instance (Other-tab slider, `05`). Defaults to `Environment.ProcessorCount`; missing/corrupt/out-of-range values are clamped to `1..ProcessorCount` on load.
- Tolerant load: missing/corrupt file → defaults; save is best-effort + atomic temp-write.
- Registration order matters: `ISettingsStore` can be constructed **before** `IAppPaths` because it does not depend on the data dir.

## 4. `IAppPaths` + `IAppPathsFactory`

```csharp
public interface IAppPaths
{
    string DataDirectory { get; }    // root of db/instance
    string DbDirectory { get; }      //  <root>\db
    string InstanceDirectory { get; }//  <root>\instance
    void EnsureCreated();
}

public interface IAppPathsFactory
{
    IAppPaths Create(string dataDirectory);
}
```

- `DataDirectory` = `settings.DataDirectory ?? Environment.CurrentDirectory`. The env-var override from rev 1 is **dropped** (superseded by the setting).
- `EnsureCreated()` mirrors v2 `CheckDirs`; called at startup.
- The factory lets composition build `IAppPaths` **after** first-launch resolution (below), and lets tests build paths for arbitrary roots.

## 5. First-launch & V2 migration (`FirstLaunchService`)

Runs once at startup, before any UI feature work, per the launch matrix:

| Scenario | Behavior |
|---|---|
| Settings file exists | Use `DataDirectory ?? CWD`; no dialog; skip everything below. |
| First launch + CWD already has `db/` + `instance/` (typical in-place V2 upgrade) | **Auto-adopt CWD** (no prompt), import legacy dark theme (see below), save settings, proceed. |
| First launch + no V2 data in CWD | Prompt via `IDialogService` for the data dir (default = CWD); if the chosen dir contains V2 `db/`/`instance/`, adopt in place; save settings. |

```csharp
public enum FirstLaunchAction { UseExistingSettings, AdoptCurrentDirectory, PromptForDirectory }

public sealed record FirstLaunchResult(
    FirstLaunchAction Action,
    string DataDirectory,
    bool HasLegacyV2Data,
    bool? LegacyDarkTheme);
```

- **Legacy dark theme import (D2):** if `{DataDirectory}\settings.json` exists (V2 leftover), read its `IsDarkTheme` and fold it into the new `AppSettings`. The old file is left untouched (non-destructive migration).
- Detection logic is pure and unit-testable (`Directory.Exists(db) && Directory.Exists(instance)` at a candidate root).
- The dialog itself lives in App (`IDialogService`); `FirstLaunchService` stays UI-free.
- UI presentation of the prompt is specified in `08-viewmodels-ui.md`.

## 6. `IInstanceRepository`

```csharp
public interface IInstanceRepository
{
    Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default);
    Task<Instance?> GetAsync(string name, CancellationToken ct = default);
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);  // OrdinalIgnoreCase
    Task SaveAsync(Instance instance, CancellationToken ct = default);
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
}
```

- File layout: `Path.Combine(instanceDir, name + ".json")`; rejects names with separators (defense in depth; names already pass `InstanceNameValidator`).
- Load: `InstanceJson.Options`, normalize null collections → empty; missing file → `null`; malformed JSON → `InstanceStorageException` (typed, includes file path).
- Save: recompute `TotalFileSizeString` (02 D3), then **atomic temp-file + `File.Move(overwrite)`** (D3) - safer than v2's direct write.
- `Exists`/uniqueness: `OrdinalIgnoreCase` on all platforms (Windows/Linux interchangeable). `GetAllAsync` sorted by name.

## 7. `IFileStore` (dedup store)

```csharp
public interface IFileStore
{
    bool Exists(string hashedFileName);
    string GetPath(string hashedFileName);
    Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default);
    Task CopyOutAsync(string hashedFileName, string destinationPath, CancellationToken ct = default); // skip if exists
    Task DeleteAsync(string hashedFileName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default);
    Task<long> GetTotalSizeAsync(CancellationToken ct = default);
}
```

- All names resolve to `Path.Combine(dbDir, hashedFileName)`.
- **Traversal guard (D4):** names must match `^[0-9A-F]{32}(\.[^\\/]+)?$` before touching the FS.
- Dedup on write (skip if exists); `CopyOutAsync` never overwrites; move-mode is handled in `InstanceService` via `CopyToStoreAsync` + hard-link back (05).
- `GetTotalSizeAsync` / `GetAllHashedFileNamesAsync` feed status panel + unused-files scan.

## 8. `InstanceJson` (serialization helper)

`Infrastructure/Serialization/InstanceJson.cs`: shared `JsonSerializerOptions` (`WriteIndented`, default PascalCase - v2 contract) + `Normalize(Instance?)` guaranteeing non-null collections. Tested via fixture round-trips.

## 9. `LegacyImporter` (v1 `DBInfo.xml`)

v1 DTOs (`DBInfo` / `DataInstance` / `InstanceFileInfo`) internal to the file; `RelativePath` = `Location` minus leading `\`; `DirectoryList` derived from file paths (v2 parity); skip existing names (`OrdinalIgnoreCase`); returns `Imported`/`SkippedExisting` for UI logging. Exact v2 semantics (D5).

## 10. Errors

`InstanceStorageException` for corrupt instance JSON; IO errors propagate and are caught/reported by services.

## 11. Test plan (`tests/LincleLINK.Core.Tests/`)

**`Settings/JsonSettingsStoreTests`**
- round-trip incl. `DataDirectory`; missing/corrupt file → defaults; writes to per-OS config path (injected path for tests).

**`Paths/AppPathsTests`**
- default root = CWD; custom root via factory; `EnsureCreated`; immutable after construction; settings path is **not** part of `IAppPaths`.

**`Application/FirstLaunchServiceTests`**
- existing settings → `UseExistingSettings`; CWD with V2 data → `AdoptCurrentDirectory` + legacy dark-theme import; no V2 data → `PromptForDirectory`; chosen dir with V2 data adopts in place; legacy settings file untouched.

**`Instances/JsonInstanceRepositoryTests`**
- save → file byte-identical to v2 fixture (ignore line endings); save recomputes `TotalFileSizeString`; load round-trip; missing → null; `ExistsAsync` case-insensitive; delete true/false; malformed JSON → `InstanceStorageException`; name with path chars rejected.

**`Storage/FileStoreTests`**
- copy-to-store dedups (exists → no-op); move removes source; copy-out skips existing; link-out delegates to `IHardLinker` (mock); delete; totals; `GetAllHashedFileNames`; traversal names (`..\x`, `a/b`) rejected.

**`Application/LegacyImporterTests`**
- sample `DBInfo.xml` fixture → correct `Instance` fields (incl. leading-`\` strip); existing-name skip; directory list derived from file paths.

## 12. Decisions (locked)

- **D1** Settings live in per-OS app-config dir; data dir split out.
- **D2** Non-destructive V2 legacy dark-theme import at first launch.
- **D3** Atomic instance save (temp + move).
- **D4** `FileStore` hash-name guard regex `^[0-9A-F]{32}(\.[^\\/]+)?$`.
- **D5** Port v2 legacy-import semantics exactly.
- **D6** Env-var override dropped; `DataDirectory` setting is the single mechanism.
