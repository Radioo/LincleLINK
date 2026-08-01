# AGENTS.md

## Status: v2 → v3 rewrite in progress

- The approved rewrite plan lives in `docs/plan/` (`00-high-level.md` … `12-verification.md`). **Read `00` first** — §14 is the expansion index, §12 the milestone order. Each numbered doc is the locked spec for one area.
- Target v3: Avalonia cross-platform (Windows + Linux), .NET 10 LTS, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, **Semi.Avalonia** theme (not Fluent), MonoTorrent, layered `src/LincleLINK.Core` / `src/LincleLINK.App` / `tests/`.
- **The current tree is still the v2 WPF app and stays that way until M0 executes.** Do not add features to the v2 code; work happens per the plan, in milestone order. `12-verification.md` §6 is the register of deliberate v2→v3 behavior changes.
- The v2 guidance below describes only the pre-rewrite tree and is replaced by v3 once M0 restructures the repo (`12` §7, `01` §9).

## Build & verify

- v2 (current tree): `dotnet build WPFLinkTool.sln` — no tests, no linter, no CI. Build is the only verification.
- v3 (after M0): `dotnet build LincleLINK.sln` + `dotnet test LincleLINK.sln`; CI matrix on Windows + Linux (`11`).
- The v2 build emits ~120 pre-existing CS8618/CS860x nullable warnings in `LincleLINK/Logic/` — don't try to fix them all.
- **Do NOT test UI changes via screenshots/render-capture harnesses** — that has wasted hours and produces misleading artifacts (chrome bands, stale frames). Ask the user to test and report instead.

## Two projects — only one is active (v2)

- `LincleLINK/` — active app (v2, JSON storage). All current work here.
- `WPFLinkTool/` — legacy v1 app (XML storage). Keep-only; don't add features. LincleLINK imports its `DBInfo.xml` via `ImportLegacyInstances` (`MainWindowLogic.cs:439`).
- The solution file is still named `WPFLinkTool.sln` and contains both.

## Architecture (v2, no MVVM framework)

- Windows are code-behind; `MainWindow.xaml.cs:28` constructs `MainWindowLogic` and hands it UI controls via the `MainWindowControls` struct. `AddInstanceWindow` follows the same pattern with `AddInstanceWindowLogic`.
- The `Logic/` classes ARE the view models (`MainWindowLogic` = main window, `AddInstanceWindowLogic` = add-instance dialog). `Logic/Base/` has hand-rolled `RelayCommand`, `AsyncRelayCommand`, `AsyncCommandBase`, `PropertyChangedBase`. No `CommunityToolkit.Mvvm` or similar.
- Long-running work: `async void` handlers guarded by the single `IsFree` flag (no concurrency); commands opt out via `CanDoActionWithInstance`. UI updates marshal through the captured `SynchronizationContext` (`UIContext`).
- Windows Forms is intentionally mixed in (`UseWindowsForms=true`): `FolderBrowserDialog`, `MessageBox`, `OpenFileDialog`, `DriveInfo`. Don't "modernize" these away.
- v3 replaces all of the above (see `08`, `09`); don't let v2 patterns leak into new code written per the plan.

## Runtime data layout (v2)

Paths are relative to `Directory.GetCurrentDirectory()` and created on startup (`CheckDirs`, `MainWindowLogic.cs:351`). `MainWindowLogic.cs:20-24` has commented-out hardcoded test paths — uncomment to run against a scratch dir instead of CWD.

- `db/` — deduplicated file store; files named `<UPPERCASE_MD5_HEX><original extension>` (lowercase dir name, unlike legacy v1's `DB`). Hashed via `AddInstanceWindowLogic.GetMD5Checksum` (`BitConverter.ToString` → uppercase, no dashes).
- `instance/<InstanceName>.json` — one manifest per instance, System.Text.Json (indented). `Instance`/`InstanceFile` types in `Logic/DataTypes.cs`. Instance names must be filesystem-safe (validated in `AddInstanceWindowLogic.ValidateInstance`).
- `settings.json` — ad-hoc, untracked, in CWD; stores `{"IsDarkTheme": bool}` (key is `IsDarkTheme`, not `IsDark`). Written by `ThemeManager.SaveSettings`.
- v3 splits this: user data stays CWD-relative (`db/`, `instance/`), settings move to the per-OS config dir with an added `DataDirectory` setting (`03`).

## Theming (v2)

- `Themes/LightTheme.xaml` and `Themes/DarkTheme.xaml` are resource dictionaries; `Themes/ThemeManager.cs` swaps them at runtime (`ApplyTheme(bool)`), applies the DWM immersive dark title bar (`DwmSetWindowAttribute` attr 20, fallback 19), and persists to `settings.json`.
- `App.xaml.cs` `OnStartup` calls `ThemeManager.ApplyTheme(ThemeManager.IsDark)` before `base.OnStartup` (no theme flash).
- `MainWindowLogic.IsDarkTheme` is the VM property backing the "Dark mode" CheckBox on the Other tab; its setter calls `ApplyTheme` then `OnPropertyChanged`.
- Both windows hook `SourceInitialized` → `ThemeManager.ApplyImmersiveTitleBar(this)`.
- The dictionaries override `SystemColors.*BrushKey` (via DynamicResource) so Aero2-based controls follow: Window/Control/ControlText/Highlight/Text/GrayText/Info/InactiveSelectionHighlight. Controls that hardcode colors (Button, TabItem, ComboBox, ProgressBar, ScrollBar, ToolTip, DataGrid parts) get explicit implicit styles with our own ControlTemplates — keep both themes in sync.
- `MainWindow.xaml` window background, the GridSplitter, and the status ProgressBar foreground bind to theme brushes via `{DynamicResource ...}`. `AddInstanceWindow` too.
- v3 replaces this with Semi.Avalonia + `ThemeVariant` (`09`) — do not port the WPF dictionary approach.

## Dangerous / irreversible operations

- Hard links (`CreateHardLink` Kernel32 P/Invoke, `MainWindowLogic.cs:822`) only work on the same partition. Per README: to edit or replace a hard-linked file, delete it first, or you modify the originals in `db/`.
- `CreateHardLinks` deletes duplicate files in the target dir before linking (after a Yes/No prompt).
- `CheckForUnusedFiles` permanently deletes `db/` files referenced by no instance.
- Torrent flow: `CheckFiles` (name+size match) → `CheckPieces` (byte-exact piece hashes via `TorrentPiecer` in `DataTypes.cs`) → `LinkToTorrent`. Only files whose pieces all match are linked, so the torrent client won't overwrite originals. Relative path inside the torrent is usually `contents\data`.
- These semantics carry into v3 (with documented fixes in `05`–`07`); keep the safety properties.

## Style

- Block-scoped namespaces (not file-scoped); target-typed `new()`; `Nullable` enabled.
- Namespaces don't follow folders: `Logic/*.cs` uses `namespace LincleLINK` (a couple use `LincleLINK.Logic`) — match the file you're editing.
- Only NuGet dependency in v2: `BencodeNET` 4.0.0 (torrent parsing) — replaced by MonoTorrent in v3.
- v3 style differs (`01`): file-scoped namespaces, layered namespaces, TreatWarningsAsErrors on Core/Tests.
