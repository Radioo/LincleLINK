# AGENTS.md

## Status: v3 rewrite in progress (M0–M6 done, M7 SQLite storage started)

- The locked rewrite plan lives in `docs/plan/` (`00-high-level.md` … `12-verification.md`). **Read `00` first** — §14 is the expansion index, §12 the milestone order. Work happens per the plan, in milestone order; don't invent features outside it.
- **M0–M6 are done and committed** on branch `rewrite`: v3 skeleton, Core (domain/storage/ports), add-instance, linking/unused-files/legacy import, torrent flow (MonoTorrent), theming/title-bar/log, test hardening + docs. **M7 (`13-sqlite-storage.md`) in progress on `feature/sqlite-db`**: instance metadata moves to a SQLite DB (`linclelinc.db`) via EF Core code-first; older users get a forced one-time migration at launch (JSON → SQLite, then JSON deleted).
- Target: Avalonia cross-platform (Windows + Linux + macOS), .NET 10 LTS, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, **Semi.Avalonia** (not Fluent), MonoTorrent, layered `src/LincleLINK.Core` / `src/LincleLINK.App` / `tests/`.
- `docs/plan/12-verification.md` §2 is the feature-parity matrix, §6 the consolidated v2→v3 behavior-change register.

## Build & verify

- Build: `dotnet build LincleLINK.sln`; tests: `dotnet test LincleLINK.sln`.
- CI: GitHub Actions matrix (`windows-latest` + `ubuntu-latest` + `macos-latest`) — `.github/workflows/ci.yml`.
- `TreatWarningsAsErrors` is on for Core + both test projects (off for App). Keep builds at 0 warnings.
- **Do NOT test UI changes via screenshots/render-capture harnesses** — ask the user to test and report instead.

## Layout

- `src/LincleLINK.Core/` — platform-agnostic, no Avalonia/WPF refs: `Domain/` (pure models), `Abstractions/` (ports/interfaces), `Application/` (use cases), `Infrastructure/` (adapters incl. `Persistence/` for the EF Core SQLite metadata DB), `Composition/` (`AddLincleLINKCore()`).
- `src/LincleLINK.App/` — Avalonia UI: `Views/`, `ViewModels/` (CommunityToolkit.Mvvm), `Services/` (DialogService, ThemeManager), `Composition/AppBootstrapper.cs` (DI root), `Styles/`.
- `tests/LincleLINK.Core.Tests/`, `tests/LincleLINK.App.Tests/` — xUnit + FluentAssertions + NSubstitute.
- Central Package Management: versions pinned in `Directory.Packages.props`; project files reference packages versionless.
- Shared props in `Directory.Build.props` (net10.0, nullable, implicit usings, version 3.0.0).

## Conventions

- File-scoped namespaces; namespaces by layer (`LincleLINK.Core.Domain`, `LincleLINK.App.ViewModels`, …).
- Services depend on ports (`Abstractions/`), never on UI; VMs stay thin binding shells; dialogs go through `IDialogService`.
- Async commands use `IProgress<T>` + `CancellationToken`; no `async void`.
- Data layout: user data lives under a data dir (default CWD, configurable via settings `DataDirectory`), `db/` + `instance/`; settings in the per-OS config dir (`03`).

## Gotchas

- .NET 10 `dotnet new sln` defaults to `.slnx` — pass `-f sln` for the classic format this repo uses.
- Avalonia 12: diagnostics come from `AvaloniaUI.DiagnosticsSupport` (not `Avalonia.Diagnostics`).
- Semi.Avalonia styles are registered in `App.axaml` (`<semi:SemiTheme/>` + `<semi:DataGridSemiTheme/>`); theme switches via `IThemeManager`/`RequestedThemeVariant`.
- EF Core (M7): migrations are committed under `Infrastructure/Persistence/Migrations/` and applied at runtime via `Database.MigrateAsync()` — generate with `dotnet ef` (global tool, version-matched; `IDesignTimeDbContextFactory` lets it run against Core without launching Avalonia). The DB file lives at the data root, never inside `db/` (that dir is scanned by `IFileStore`).
