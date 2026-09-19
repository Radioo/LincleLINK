# AGENTS.md

## Build & verify

- Build: `dotnet build LincleLINK.sln`; tests: `dotnet test LincleLINK.sln`.
- CI: GitHub Actions matrix (`windows-latest` + `ubuntu-latest` + `macos-latest`) - `.github/workflows/ci.yml`.
- `TreatWarningsAsErrors` is on for Core + both test projects (off for App). Keep builds at 0 warnings.
- **Do NOT test UI changes via screenshots/render-capture harnesses** - ask the user to test and report instead.

## Layout

- `src/LincleLINK.Core/` - platform-agnostic, no Avalonia/WPF refs: `Domain/` (pure models), `Abstractions/` (ports/interfaces), `Application/` (use cases), `Infrastructure/` (adapters incl. `Persistence/` for the EF Core SQLite metadata DB), `Composition/` (`AddLincleLINKCore()`).
- `src/LincleLINK.App/` - Avalonia UI: `Views/`, `ViewModels/` (CommunityToolkit.Mvvm), `Services/` (DialogService, ThemeManager), `Composition/AppBootstrapper.cs` (DI root), `Styles/`.
- `tests/LincleLINK.Core.Tests/`, `tests/LincleLINK.App.Tests/` - xUnit + FluentAssertions + NSubstitute.
- Central Package Management: versions pinned in `Directory.Packages.props`; project files reference packages versionless.
- Shared props in `Directory.Build.props` (net10.0, nullable, implicit usings, version 3.0.0).

## Conventions

- **Never use em dashes (—)** - not in code, strings, comments, docs, or UI text. Use a plain hyphen, colon, or rephrase.
- File-scoped namespaces; namespaces by layer (`LincleLINK.Core.Domain`, `LincleLINK.App.ViewModels`, …).
- Services depend on ports (`Abstractions/`), never on UI; VMs stay thin binding shells; dialogs go through `IDialogService`.
- Async commands use `IProgress<T>` + `CancellationToken`; no `async void`.
- **The UI never freezes, and everything that takes time shows a progress indicator.** Nothing slow (database, disk, hashing, anything sized by an entry's file count) runs on the UI thread; `await` alone does not move it, and SQLite's async calls are synchronous underneath. Full rule and checklist: `CLAUDE.md`.
- Data layout: user data lives under a data dir (default CWD, configurable via settings `DataDirectory`), `db/` + `instance/`; settings in the per-OS config dir (`03`).

## Gotchas

- .NET 10 `dotnet new sln` defaults to `.slnx` - pass `-f sln` for the classic format this repo uses.
- Avalonia 12: diagnostics come from `AvaloniaUI.DiagnosticsSupport` (not `Avalonia.Diagnostics`).
- Semi.Avalonia styles are registered in `App.axaml` (`<semi:SemiTheme/>` + `<semi:DataGridSemiTheme/>`); theme switches via `IThemeManager`/`RequestedThemeVariant`.
- Semi's `ProgressBar` has a default `MinWidth` of 200 that beats `Width` and a narrow grid column. A bar given less room spills out of it and the text next to it is drawn on top of the bar. Every `ProgressBar` gets `MinWidth="0"`, and a sized bar sits in an `Auto` column with an explicit `Width`. `ProgressLayoutTests` (App.Views.Tests) lays the views out with the real theme and fails on any text overlapping a bar or another text.
- EF Core (M7): migrations are committed under `Infrastructure/Persistence/Migrations/` and applied at runtime via `Database.MigrateAsync()` - generate with `dotnet ef` (global tool, version-matched; `IDesignTimeDbContextFactory` lets it run against Core without launching Avalonia). The DB file lives at the data root, never inside `db/` (that dir is scanned by `IFileStore`).
