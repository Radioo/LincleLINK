# 01 — Project Structure

**Parent:** [00-high-level.md](00-high-level.md) §4 (Solution Layout), §5 (Architecture), §10 (Extensibility)
**Milestone:** M0
**Status:** Approved

## 1. Scope

Define the concrete on-disk layout, project files, namespaces, package management, DI composition, and build/test commands for the v3 repo. Restructures the repo: `WPFLinkTool/` (v1) and `LincleLINK/` (v2) are removed and replaced.

## 2. Repository layout

```
LincleLINK.sln                      (renamed from WPFLinkTool.sln)
Directory.Build.props               (shared MSBuild props for all projects)
Directory.Packages.props            (Central Package Management - version pins)
README.md                           (rewritten in M6)
AGENTS.md                           (rewritten in M6)
docs/plan/                          (this planning tree)
├─ src/
│  ├─ LincleLINK.Core/              net10.0 class library, zero UI deps
│  └─ LincleLINK.App/               net10.0 Avalonia desktop app
└─ tests/
   ├─ LincleLINK.Core.Tests/          net10.0 xUnit test project
   └─ LincleLINK.App.Tests/           net10.0 VM logic tests, no rendering (08 D6)
```

Solution folders `src/` and `tests/` group the projects in the `.sln`.

## 3. Shared build props (`Directory.Build.props`)

Applied to all three projects via the root file:

- `<TargetFramework>net10.0</TargetFramework>`
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<LangVersion>latest</LangVersion>`
- `<Version>3.0.0</Version>` (app bumps this; Core/Tests inherit)
- `<Company>LincleLINK</Company>` / `<Product>LincleLINK</Product>` (optional metadata)

Decision **D1**: enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` for Core and Tests (fresh code, small surface) while leaving it off for App initially to avoid Avalonia-generated noise.

## 4. Central Package Management (`Directory.Packages.props`)

`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` with pinned versions (baseline; exact numbers recorded when packages are first restored in M0):

| Package | Baseline |
|---|---|
| Avalonia | 12.1.x |
| Avalonia.Desktop | 12.1.x |
| Semi.Avalonia | 12.1.x |
| Semi.Avalonia.DataGrid | 12.1.x |
| Avalonia.Fonts.Inter | 12.1.x |
| Avalonia.Diagnostics | 12.1.x (Debug-only) |
| CommunityToolkit.Mvvm | latest stable (8.x) |
| Microsoft.Extensions.DependencyInjection | 10.x |
| MonoTorrent | 3.0.2 |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | latest stable |
| FluentAssertions | latest stable |
| NSubstitute | latest stable |
| coverlet.collector | latest stable |

## 5. `src/LincleLINK.Core` — platform-agnostic core

Layered folder structure, no Avalonia/WPF/Windows references.

```
LincleLINK.Core/
├─ LincleLINK.Core.csproj
├─ Composition/
│  └─ ServiceCollectionExtensions.cs   # AddLincleLINKCore() — registers Core services & infra
├─ Abstractions/                       # PORTS (interfaces only)
│  ├─ Dialogs/IDialogService.cs        # UI-independent confirm/error/message port
│  ├─ Filesystem/IFileSystem.cs        # thin file/dir facade (testable IO)
│  ├─ Hashing/IFileHasher.cs           # MD5 default
│  ├─ Instances/IInstanceRepository.cs # list/load/save/delete Instance
│  ├─ Linking/IHardLinker.cs           # platform hard-link abstraction
│  ├─ Paths/IAppPaths.cs               # resolved data dir → db/instance (see 03)
│  ├─ Settings/ISettingsStore.cs       # per-OS config; IsDarkTheme + DataDirectory (see 03)
│  ├─ Storage/IFileStore.cs            # db/ dedup store (exists/copy/move/delete by hash)
│  └─ Torrents/ITorrentSource.cs       # torrent parse + piece-hash info (see 07)
├─ Domain/                             # pure models + value logic, no IO
│  ├─ Instance.cs                      # v2-schema compatible POCO
│  ├─ InstanceFile.cs
│  ├─ InstanceListEntry.cs
│  ├─ SizeFormatter.cs                 # port of Instance.ReadableSize
│  └─ Validation/InstanceNameValidator.cs
├─ Application/                        # USE CASES (depend only on Abstractions)
│  ├─ InstanceService.cs               # add instance (hash/copy/move/dedup)
│  ├─ LinkingService.cs                # hard links, copy-hashed, dup handling
│  ├─ UnusedFilesService.cs            # find/delete unreferenced db files
│  ├─ TorrentService.cs                # check files, check pieces, link-to-torrent
│  ├─ TorrentPieceVerifier.cs          # streaming piece hashing (see 07)
│  ├─ StatusService.cs                 # db size / savings / free space summary (see 08)
│  ├─ FirstLaunchService.cs            # startup resolution + V2 data adoption (see 03)
│  └─ LegacyImporter.cs                # DBInfo.xml (v1) import
└─ Infrastructure/                     # ADAPTERS (concrete impls)
   ├─ Filesystem/FileSystem.cs
   ├─ Hashing/Md5FileHasher.cs
   ├─ Instances/JsonInstanceRepository.cs
   ├─ Linking/Win32HardLinker.cs       # CreateHardLinkW P/Invoke (Windows)
   ├─ Linking/UnixHardLinker.cs        # link() P/Invoke (Linux)
   ├─ Paths/AppPaths.cs
   ├─ Settings/JsonSettingsStore.cs
   ├─ Storage/FileStore.cs
   ├─ Torrents/MonoTorrentSource.cs
   └─ Disk/DriveInfoProvider.cs        # + Linux statvfs impl (see §7)
```

Rules:

- `Domain/` references nothing outside itself. `Application/` references only `Abstractions` + `Domain`. `Infrastructure/` implements `Abstractions`.
- `IDialogService` lives in Core as a port; the Avalonia implementation is registered by App (§7). This keeps mid-operation confirmations (e.g. "delete duplicate files?") testable.
- Namespaces: `LincleLINK.Core`, `LincleLINK.Core.Domain`, `LincleLINK.Core.Abstractions.*`, `LincleLINK.Core.Application`, `LincleLINK.Core.Infrastructure.*`. **File-scoped** namespaces for all new code (replaces the v2 block-scoped style).
- `LincleLINK.Core.csproj`: no `OutputType`, no `UseWPF`/`UseWindowsForms`, no `TargetFramework` attribute (inherited), `[SupportedOSPlatform]` guards only where needed (hard linkers, statvfs).

## 6. `src/LincleLINK.App` — Avalonia UI

```
LincleLINK.App/
├─ LincleLINK.App.csproj
├─ Program.cs                          # BuildAvaloniaApp(): UsePlatformDetect, WithInterFont, LogToTrace
├─ App.axaml / App.axaml.cs            # resources, styles, ViewLocator registration
├─ ViewLocator.cs                      # VM → View name-convention IDataTemplate
├─ app.manifest                        # Windows DPI awareness (ignored on Linux)
├─ Assets/                             # LL_logo.ico (moved from v2) + window icon
├─ Abstractions/                       # App-side ports shared by VMs and Services
│  ├─ IAppDialogHost.cs                # hosts any VM's View via ViewLocator (see 08)
│  ├─ IThemeManager.cs                 # light/dark switch port (see 09)
│  ├─ IDialogViewModel.cs              # VM contract for dialog-window hosting
│  └─ IOperationHost.cs                # shared busy/log/operation runner for feature VMs
├─ Composition/
│  └─ AppBootstrapper.cs               # builds ServiceProvider: AddLincleLINKCore() + App services
├─ Services/
│  ├─ DialogService.cs                 # Avalonia impl of IDialogService + IAppDialogHost
│  ├─ ThemeManager.cs                  # IThemeManager impl + title-bar theming
│  └─ Win32DarkTitleBar.cs             # native Windows title-bar darkening
├─ Controls/
│  └─ MessageDialog.axaml              # OK/YesNo message box (Confirm/Info/Error, see 08)
├─ Behaviors/
│  └─ AutoScrollBehavior.cs            # attached behavior: pin log list to bottom
├─ ViewModels/
│  ├─ Base/ViewModelBase.cs            # ObservableObject (CommunityToolkit.Mvvm) + shared theme pair
│  ├─ MainViewModel.cs                 # shell VM for MainWindow (instance/linking orchestration)
│  ├─ TorrentCheckViewModel.cs         # "Link to torrent" tab (paths, gates, commands)
│  ├─ AddInstanceViewModel.cs
│  └─ FirstRunViewModel.cs             # first-launch data-dir prompt (see 03/08)
├─ ProgressBridge.cs                   # IProgress<T> factory (sync in tests, marshaled/batched in UI)
└─ Views/
   ├─ MainWindow.axaml / .axaml.cs
   ├─ AddInstanceWindow.axaml / .axaml.cs
   └─ FirstRunWindow.axaml / .axaml.cs
```

Key decisions:

- csproj: `<OutputType>WinExe</OutputType>` (works on both OS), `<BuiltInComInteropSupport>true</BuiltInComInteropSupport>`, `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`, `<AvaloniaResource Include="Assets\**"/>`.
- `Program.cs` keeps the static `BuildAvaloniaApp()` helper (test/headless-friendly).
- `App.axaml.cs` `OnFrameworkInitializationCompleted`: resolve `MainViewModel` from the bootstrapped provider, set as `MainWindow.DataContext`. No service resolution scattered in views.
- Dialog windows use Avalonia `Window`; file/folder pickers use `StorageProvider` (cross-platform). No WinForms.
- ViewModels use `[ObservableProperty]` / `[RelayCommand]` / `[AsyncRelayCommand]` source generators. Async commands get proper `IProgress<double>` + `CancellationToken` handling — the v2 `async void` + `IsFree` pattern is replaced by an operation-gating design (details in `08-viewmodels-ui.md`).
- Namespaces: `LincleLINK.App`, `LincleLINK.App.Abstractions`, `LincleLINK.App.ViewModels` (+ `.ViewModels.Base`), `LincleLINK.App.Views`, `LincleLINK.App.Services`, `LincleLINK.App.Composition`. File-scoped.

## 7. Composition / DI split

- **Core** exposes `ServiceCollectionExtensions.AddLincleLINKCore(IServiceCollection)` registering: all application services (transient/scoped), all infrastructure adapters, `IAppPaths`, `ISettingsStore`, `IFileHasher`, `IInstanceRepository`, `IFileStore`, `IFileSystem`, `IDriveInfoProvider`, `ITorrentSource`, and the platform-appropriate `IHardLinker` (selected via `OperatingSystem.IsWindows()`). Core registers **no** `IDialogService`.
- **App** (`AppBootstrapper`) calls `AddLincleLINKCore()` then registers `IDialogService` → `DialogService` and the ViewModels. App is the only place with Avalonia references.
- Hard-linker selection logic (Windows kernel32 vs Linux libc) is centralized in Core so tests can target either implementation explicitly.

## 8. `tests/LincleLINK.Core.Tests`

```
LincleLINK.Core.Tests/
├─ LincleLINK.Core.Tests.csproj
├─ TestHelpers/
│  ├─ TempDir.cs                       # IDisposable temp-root test fixture
│  └─ TestData.cs                      # small-file builders, sample instance JSON (v2 fixtures)
├─ Domain/
│  ├─ SizeFormatterTests.cs
│  └─ InstanceNameValidatorTests.cs
├─ Instances/JsonInstanceRepositoryTests.cs
├─ Storage/FileStoreTests.cs
├─ Hashing/Md5FileHasherTests.cs
├─ Torrents/MonoTorrentSourceTests.cs  # uses generated .torrent fixtures
├─ Disk/DriveInfoProviderTests.cs
└─ Application/
   ├─ InstanceServiceTests.cs
   ├─ LinkingServiceTests.cs
   ├─ UnusedFilesServiceTests.cs
   ├─ TorrentServiceTests.cs
   ├─ LegacyImporterTests.cs           # ships a sample DBInfo.xml fixture
   └─ StatusServiceTests.cs

tests/LincleLINK.App.Tests            # logic-only VM tests, no rendering (see 08 D6)
├─ LincleLINK.App.Tests.csproj
└─ MainViewModelTests.cs / AddInstanceViewModelTests.cs
```

- Packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions, NSubstitute, coverlet.collector.
- Filesystem-backed tests use `TempDir` with real files at small scale; ports (`IHardLinker`, `IDialogService`, `IDriveInfoProvider`) mocked via NSubstitute.
- Verification commands (replaces v2's build-only check):
  - `dotnet build LincleLINK.sln`
  - `dotnet test LincleLINK.sln`

## 9. Repo restructure mechanics (executed at M0 start)

1. Delete `WPFLinkTool.sln`, `LincleLINK/`, `WPFLinkTool/` (preserved in git history).
2. `dotnet new` the projects (or hand-write csproj) under `src/`/`tests/`, add to a new `LincleLINK.sln` with `src`/`tests` solution folders.
3. Add `Directory.Build.props`, `Directory.Packages.props`.
4. Move `LL_logo.ico` → `src/LincleLINK.App/Assets/`.
5. Skeleton app (empty MainWindow + MainViewModel) must **build and launch on Windows and Linux** before any feature work.
6. `.gitignore` already covers `bin/obj`; add `*.user`/`.vs` as needed — no structural changes required.

## 10. What this plan does NOT decide (deferred)

- Exact package version numbers — locked during M0 restore.
- `IAppPaths` semantics (data root from `settings.DataDirectory` + first-launch V2 adoption) → `03-storage-repository.md` (resolved).
- Command gating / operation-state design → `08-viewmodels-ui.md`.
- Theme resource details → `09-theming.md`.
- CI workflow file → `11-ci.md`.

## 11. Decisions (locked)

- **D1** `TreatWarningsAsErrors` on for Core/Tests, off for App.
- **D2** Central Package Management: yes.
- **D3** `IDialogService` port lives in Core, Avalonia adapter in App.
- **D4** File-scoped namespaces everywhere new.
- **D5** `src/` + `tests/` solution folders.
