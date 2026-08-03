# LincleLINK v3 — High-Level Rewrite Plan

Status: **Draft for review.** This is the root document. Every numbered section will be expanded into its own detailed sub-plan under `docs/plan/`, reviewed and locked before implementation begins.

## 1. Vision & Goals

Rewrite LincleLINK from a WPF/.NET 6 Windows-only app into a modern, cross-platform application:

- **Cross-platform**: Windows + Linux from day one (Avalonia makes macOS/others reachable later)
- **Professional codebase**: layered architecture, port/abstraction boundaries, DI, no `Logic/` monolith
- **Testable**: unit tests for core logic and services
- **Feature parity** with v2 (instances, dedup store, hard-link linking, torrent-aware linking, legacy import)
- **Future-extensible**: swappable services behind interfaces (hashing, linking strategy, torrent source, storage)

## 2. Non-Goals (this pass)

- No macOS/Android/iOS targets initially (kept possible by Avalonia + platform-agnostic Core)
- No changes to the v2 on-disk data model — existing `db/` and `instance/*.json` data must remain valid
- No new networking/cloud features; torrent feature remains local-file matching only

## 3. Confirmed Decisions

| Area | Decision |
|---|---|
| Runtime | .NET 10 (LTS, supported to Nov 2028) |
| UI | Avalonia 12.x (pin 12.1.x at implementation time) |
| MVVM | CommunityToolkit.Mvvm (source generators) |
| DI | Microsoft.Extensions.DependencyInjection |
| Torrent | MonoTorrent (stable 3.0.2; parsing + piece hashing, cross-platform) |
| Data location | User data keeps the V2 CWD-relative layout: `db/`, `instance/` (backward compatible). App settings move to per-OS config dir and gain a `DataDirectory` setting; first-launch flow adopts existing V2 data (see `03`) |
| Testing | xUnit + FluentAssertions + NSubstitute |
| Legacy | Replace `WPFLinkTool` (v1) and old `LincleLINK` (v2) in this repo; history preserved via git |

## 4. Solution Layout

Rename solution `WPFLinkTool.sln` → `LincleLINK.sln`:

```
LincleLINK.sln
├─ src/
│  ├─ LincleLINK.Core/      # platform-agnostic: domain models, ports, application services, infra impls (net10.0)
│  └─ LincleLINK.App/       # Avalonia UI: Program.cs, Views, ViewModels, App.axaml, Themes (net10.0)
└─ tests/
   └─ LincleLINK.Core.Tests/ # unit + filesystem-backed tests (net10.0)
```

`LincleLINK.Core` has **zero UI dependencies**; `LincleLINK.App` references Core only.

## 5. Architecture Overview (layered, ports & adapters "lite")

```
┌──────────────────────────── UI (Avalonia) ────────────────────────────┐
│ Views  ⇄  ViewModels (CommunityToolkit.Mvvm)  ⇄  DialogService        │
└──────────────────────────────────┬────────────────────────────────────┘
┌────────────────── Application services (use cases) ──────────────────┐
│ InstanceService │ LinkingService │ TorrentService │ UnusedFilesService│
└──────────────────────────┬──────────────────────────┬─────────────────┘
┌───────────────── Domain models (pure POCOs) ─────────────────────────┐
│ Instance, InstanceFile, InstanceListEntry, Path/Size helpers, enums   │
└──────────────────────────┬──────────────────────────┬─────────────────┘
┌─────────────── Infrastructure / Ports (interfaces + impls) ──────────┐
│ IFileStore │ IInstanceRepository │ IHardLinker │ IFileHasher           │
│ IFileSystem │ IDriveInfoProvider │ IDialogService │ ISettingsStore     │
│ ITorrentSource (MonoTorrent impl)                                     │
└───────────────────────────────────────────────────────────────────────┘
```

Key rules:

- ViewModels orchestrate services; services depend on ports, never on Avalonia/WPF
- All long-running work uses async/await + proper `CancellationToken` and progress callbacks; no more `async void` handlers or magic `IsFree` flags
- Single `App.CompositionRoot` wires everything in `Program.cs`

## 6. Cross-Platform Mapping (current Windows-only code)

| Current (WPF/Windows) | Location (v2) | New abstraction | New impls |
|---|---|---|---|
| `CreateHardLink` (Kernel32 P/Invoke) | `MainWindowLogic.cs:832` | `IHardLinker` | `Win32HardLinker` (kernel32) · `UnixHardLinker` (`link()` libc P/Invoke) |
| `FolderBrowserDialog` / `OpenFileDialog` | `MainWindowLogic.cs`, `AddInstanceWindowLogic.cs` | `IDialogService` | Avalonia `StorageProvider` |
| `MessageBox` | throughout | `IDialogService` | Avalonia `Interaction`/dialog host |
| `DriveInfo` free space | `MainWindowLogic.cs:779` | `IDriveInfoProvider` | platform impls (Windows + Linux) |
| `Path.GetInvalidFileNameChars` instance-name validation | `AddInstanceWindowLogic.cs:160` | `InstanceNameValidator` (Domain static) | cross-platform-safe Windows-compatible set (see `02`) |
| Backslash `\` in `InstanceFile.RelativePath` | stored JSON | `PathNormalizer` | normalize on read/write; storage stays v2-compatible |
| CWD-relative `db`/`instance`; per-OS config `settings.json` | `MainWindowLogic.cs:20-24` | `IAppPaths` / `ISettingsStore` | `IAppPaths` keeps identical `db`/`instance` layout; settings move out of the data dir (see `03`) |
| Legacy `DBInfo.xml` import | `MainWindowLogic.cs:449` | `LegacyImporter` (XML) | in Core |

## 7. Theming

- **Semi.Avalonia** (12.1.x, + `Semi.Avalonia.DataGrid`) as the theme — no FluentTheme, no hand-rolled control templates; V2 layout kept, Semi default styling (sizes/spacing) used (see `09`)
- Light/dark via `ThemeVariant`/`RequestedThemeVariant`, switched through an app-side `IThemeManager`
- Windows native title bar darkened by a small DWM helper (verify if needed at M5; Linux no-op)
- Persist choice in the per-OS `settings.json` (`IsDarkTheme`, alongside `DataDirectory`; see `03`) for continuity

## 8. Testing Strategy

- **Pure logic** (fast, no IO): size formatting, instance JSON schema round-trip (v2 compatibility fixtures), path normalization, validation rules, torrent file→piece mapping
- **Filesystem-backed** (real temp dirs, small scale): add-instance hashing/copy/move, dedup store, unused-file detection, linking duplicate detection
- **Mocked ports**: `IHardLinker`, `IDialogService`, `IDriveInfoProvider` via NSubstitute for service-level tests
- Verification: `dotnet build LincleLINK.sln` + `dotnet test` (replaces the current build-only verification)

## 9. Feature Parity Checklist (v2 → v3)

- [ ] Instances tab: Add instance / Link files / Copy hashed files / Check unused / Delete + sortable DataGrid
- [ ] Add-instance dialog: name + path validation, copy-vs-move, low-disk warning, progress, live log
- [ ] Torrent tab: Check files (name+size), Check pieces (byte-exact), Link to torrent (only 100%-matched pieces)
- [ ] Other tab: db size / savings / free space, Import DBInfo.xml, dark-mode toggle
- [ ] Persistent log panel + progress bar; command enablement tied to operation state

## 10. Extensibility Hooks

- `IHardLinker` → future strategies: reflink (btrfs), symlink mode, copy fallback
- `IFileHasher` → MD5 today (hash-name compat), SHA-256 later
- `ITorrentSource` → MonoTorrent today, other parsers later
- `IInstanceRepository` / `IFileStore` → alternative backends (SQLite via EF Core — see `13`, cloud) without UI churn
- DI container makes new services/views plug in without global static state

## 11. CI

- GitHub Actions matrix: `windows-latest` + `ubuntu-latest`
- Steps: `dotnet build` → `dotnet test` on both OS → publish App artifacts per-OS (optional release workflow)

## 12. Milestones (high level)

| # | Milestone | Exit criteria |
|---|---|---|
| M0 | Scaffold solution + projects, DI composition root, CI matrix | App shell builds & runs on Windows + Linux |
| M1 | Core domain + storage | Instance save/load v2-compatible; legacy import works; tests pass |
| M2 | Add-instance flow | Hashing, dedup, copy/move, validation, low-disk warning; tests pass |
| M3 | Hard-link linking | Link/copy-hashed/delete-instance/unused-files on both OS; tests pass |
| M4 | Torrent flow (MonoTorrent) | Check files, check pieces, link-to-torrent; piece-mapping tests pass |
| M5 | Theming + parity polish | Light/dark, status panel, log, feature-parity review vs v2 |
| M6 | Test hardening + docs | Coverage of Core, README rewrite, AGENTS.md update |

Each milestone gets its own detailed sub-plan (scenario-by-scenario) before coding starts.

## 13. Open Questions (resolved in sub-plans)

- Pin exact Avalonia/MVVM-Toolkit/MonoTorrent versions during M0
- Linux behavior: case-insensitive instance-name uniqueness, path case collisions
- Keep `savings`/`free-space` semantics identical to v2 (yes, unless found buggy)
- Windows long-path / non-ASCII path handling parity

## 14. Plan Expansion Index (next files to write)

Order matters — expand in this sequence, then lock each before implementation:

1. `01-project-structure.md` (layout, csproj props, namespaces, DI registration)
2. `02-domain-models.md` (data model, JSON schema compat, validation)
3. `03-storage-repository.md` (`IInstanceRepository`, `IFileStore`, `IAppPaths`, legacy import)
4. `04-filesystem-ports.md` (`IHardLinker` win/linux, `IFileHasher`, `IDriveInfoProvider`, `PathNormalizer`)
5. `05-instance-flow.md` (add-instance use case, copy/move, dedup, low-disk)
6. `06-linking.md` (link files, copy hashed, unused files, duplicate handling)
7. `07-torrent.md` (MonoTorrent integration, check files/pieces, link to torrent)
8. `08-viewmodels-ui.md` (views, view models, dialog service, command gating, logging)
9. `09-theming.md` (FluentTheme light/dark, settings persistence)
10. `10-testing.md` (frameworks, fixtures, test plan per service)
11. `11-ci.md` (GitHub Actions matrix, publish)
12. `12-verification.md` (parity test checklist v2 vs v3, manual QA script)
13. `13-sqlite-storage.md` (EF Core SQLite instance metadata + forced one-time migration — M7)
14. `14-ux-clarity.md` (vocabulary overhaul, add-flow redesign, pre-flight + safe reclaim, result surfacing, shell refresh — M8)
