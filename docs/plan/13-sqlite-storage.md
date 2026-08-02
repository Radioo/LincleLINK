# 13 — SQLite Instance Storage (EF Core Code-First)

**Parent:** [00-high-level.md](00-high-level.md) §10 (extensibility: `IInstanceRepository` → SQLite)
**Milestone:** M7 (after M6 hardening)
**Status:** Approved

## 1. Goal

Replace the JSON instance manifests (`instance/*.json`) with a SQLite database at `<DataDirectory>/linclelinc.db`, implemented with EF Core code-first (checked-in migrations applied at startup). Older users with existing JSON data get a **forced, one-time migration** at launch: a non-dismissable progress window runs the migration and the JSON files are deleted after verified writes. New installs (no JSON) go straight to SQLite with no prompt.

## 2. Scope & non-goals

- **In scope:** instance *metadata* moves to SQLite (`Instances` / `InstanceFiles` / `InstanceDirectories` tables). The `db/` dedup file store stays flat files on disk — manifests only (owner decision), preserving hard-linking and the dedup model.
- **Out of scope:** `db/` contents in BLOBs; a `StorageMode` setting; a "decline / remind me" path — migration is forced once (owner decision); moving `settings.json`.

## 3. Why the current shape makes this low-churn

- All consumers (`MainViewModel`, `InstanceService`, `LinkingService`, `StatusService`, `UnusedFilesService`, `TorrentService`, `LegacyImporter`) depend on the `IInstanceRepository` port, never on the JSON implementation. Swapping the registered implementation requires no consumer changes.
- The SQLite file lives at the **data root** (sibling of `db/` + `instance/`), so `IFileStore`'s `db/` scans (unused-files, db-size) and its hash-name traversal guard are unaffected.

## 4. Packages (pinned in `Directory.Packages.props`)

| Package | Version | Notes |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.10 | aligned with the `10.0.10` DI line |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | `PrivateAssets=All`; powers `dotnet ef`; never leaks into App |

`packages.lock.json` files are committed (repo has `RestorePackagesWithLockFile=true`).

## 5. EF schema (code-first)

`Infrastructure/Persistence/`, migrations checked in under `Migrations/`.

```
Instances           InstanceName TEXT PK · NameKey TEXT UNIQUE · TotalFileSize INTEGER · TotalFileCount INTEGER · TotalFileSizeString TEXT
InstanceFiles       Id INTEGER PK AUTOINCREMENT · InstanceName TEXT FK→Instances ON DELETE CASCADE · Ordinal INTEGER · FileName TEXT · RelativePath TEXT · FileSize INTEGER · HashedFileName TEXT
InstanceDirectories Id INTEGER PK AUTOINCREMENT · InstanceName TEXT FK→CASCADE · Ordinal INTEGER · Value TEXT
```

- **Case-insensitive uniqueness** (matches `OrdinalIgnoreCase` today): a normalized `NameKey` column (`ToUpperInvariant`, unique index) with lookups on the key. `COLLATE NOCASE` alone is ASCII-only and would mishandle non-ASCII instance names.
- **`Ordinal`** columns preserve JSON array order (round-trip fidelity for `FileList` / `DirectoryList`).
- Delete of an instance cascades to child rows (manifest-only delete; `db/` files untouched, as today).

## 6. Core components

- `Infrastructure/Persistence/LincleLinkDbContext.cs` — `DbSet<InstanceEntity>` + children; `OnModelCreating` (keys, indexes, cascades); connection-level WAL + busy timeout.
- `Infrastructure/Persistence/InstanceEntity.cs`, `InstanceFileEntity.cs`, `InstanceDirectoryEntity.cs` — persistence shapes (distinct from the Domain records).
- `Infrastructure/Persistence/LincleLinkDbContextFactory.cs` — `IDesignTimeDbContextFactory<LincleLinkDbContext>` so `dotnet ef` runs against Core without launching Avalonia.
- `Infrastructure/Instances/SqliteInstanceRepository.cs` — `IInstanceRepository` impl. Registered as **singleton**, backed by `AddDbContextFactory<LincleLinkDbContext>` (a short-lived context per operation — correct EF pattern that keeps the existing singleton consumers working).
- `Application/StorageMigrationService.cs` — UI-free migration engine (see §8).
- DI (`Composition/ServiceCollectionExtensions.cs`): register `AddDbContextFactory`, `IInstanceRepository → SqliteInstanceRepository`, and `StorageMigrationService`. `JsonInstanceRepository` stays constructible (used only by the migration service and contract tests) but is no longer registered.

## 7. App changes

- `ViewModels/StorageMigrationViewModel.cs` + `Views/StorageMigrationWindow.axaml(.cs)` — informational, **non-cancellable** progress + log window ("Upgrading instance database…") reusing `IAppDialogHost` / ViewLocator machinery. `Completed` event signals the host window to close (mirrors `FirstRunViewModel`).
- Startup wiring (`App.axaml.cs` + `AppBootstrapper`): after the main container is built and `IAppPaths.EnsureCreated()` runs, if `StorageMigrationService.NeedsMigration()` → show the owner window, host the migration window modally, run `MigrateAsync` with progress/log; then set the main window content. On failure: log, keep un-migrated JSON, still open the app (never brick).

## 8. Migration semantics (`StorageMigrationService`)

- `NeedsMigration()` → any `instance/*.json` exists. (New installs never create JSON, so this is a clean signal.)
- `MigrateAsync(IProgress<string> log, IProgress<double> percent, CancellationToken ct)`:
  1. `Database.MigrateAsync()` on the SQLite context (applies any pending migrations).
  2. For each `instance/*.json` (sorted by name): parse with the existing `InstanceJson` semantics; **skip if `NameKey` already present** (idempotent — survives a crash mid-run); insert instance + ordered children in one transaction; verify via `SqliteInstanceRepository.ExistsAsync`; then **delete that JSON**.
  3. Per-file failure (unreadable/corrupt JSON) → move the file into `instance-corrupt/` with the error logged; continue. This prevents an infinite re-prompt loop next launch and never blocks the app.
  4. `percent` mirrors add-instance-style progress (migrate count / total).

## 9. Launch matrix

| Scenario | Behavior |
|---|---|
| New install (no JSON) | Straight to SQLite; no dialog |
| Existing JSON data | Forced migration window → migrate → delete JSON (quarantine corrupt) → SQLite |
| Crash mid-migration | Next launch: idempotent re-run; skips migrated names; deletes remaining JSON |
| Corrupt JSON | Quarantined + logged; app continues; no re-prompt loop |

## 10. Testing

- `tests/LincleLINK.Core.Tests/Instances/InstanceRepositoryContractTests.cs` — abstract suite run against **both** `Json` and `Sqlite` impls (SQLite via in-memory `:memory:` keep-alive connection) guaranteeing parity: round-trip, case-insensitive exists/get/delete, sorted names, `RecomputeTotals` on save, missing→null, name validation.
- `tests/LincleLINK.Core.Tests/Application/StorageMigrationServiceTests.cs` — JSON fixtures → SQLite rows; JSON deleted; corrupt file quarantined; idempotent re-run; empty-input no-op.
- `tests/LincleLINK.App.Tests/StorageMigrationViewModelTests.cs` — progress/log surface; non-cancellable; `Completed` raised at end.
- Full `dotnet build` + `dotnet test` on the three-OS CI matrix (SQLite native binaries ship cross-platform with the package).

## 11. Docs / housekeeping

- `00-high-level.md`: add `13` to §14 expansion index; note the M7 milestone.
- `12-verification.md`: new parity row (F14), §6 behavior-change entries, §4 QA steps for the migration.
- AGENTS.md: status line, layout note (`Persistence/`, `SqliteInstanceRepository`), gotcha (EF tooling), build/verify unchanged.
- README: mention SQLite storage + one-time migration.

## 12. Tooling note

Migrations are generated with `dotnet ef` (global/local tool, version-matched to the EF packages) and committed; runtime schema application is `Database.MigrateAsync()` — no `dotnet ef` needed at build/runtime.

## 13. Decisions (locked)

- **D1** Manifests only; `db/` file store stays flat files.
- **D2** Forced one-time migration at launch; no decline path, no `StorageMode` setting.
- **D3** Successful JSON files are deleted; corrupt ones are quarantined to `instance-corrupt/`.
- **D4** EF Core code-first with committed migrations applied via `Database.MigrateAsync()`.
- **D5** DB file at `<DataDirectory>/linclelinc.db` (data root), never inside `db/`.
- **D6** Case-insensitive uniqueness via normalized `NameKey` (not SQLite `NOCASE`).
- **D7** `AddDbContextFactory` + singleton repository; short-lived contexts per operation.
