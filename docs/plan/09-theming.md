# 09 — Theming (Semi.Avalonia)

**Parent:** [00-high-level.md](00-high-level.md) §7, §9; [08-viewmodels-ui.md](08-viewmodels-ui.md) §7
**Milestone:** M5
**Status:** Approved

## 1. Scope

Define the v3 theme. **No `Avalonia.Themes.Fluent`, no hand-rolled control templates.** The UI uses **Semi.Avalonia** (Avalonia theme inspired by Semi Design) with its **default control styling — including default element sizes and spacing**. The v3 contribution is limited to keeping the V2 layout structure (3 tabs / splitter / log / progress / DataGrid, plus the add-instance dialog) and wiring light/dark via settings.

## 2. Theme stack (D1)

- **Semi.Avalonia 12.1.0.1** — requires Avalonia `>= 12.1.0` (keeps the Avalonia 12.1.x pin; NuGet-compatible with net8/net9/net10). Added to package pins in `01` §4 (replaces `Avalonia.Themes.Fluent` / the draft SimpleTheme plan).
- **Semi.Avalonia.DataGrid 12.1.x** — separate Semi package for `DataGrid` styling (the app uses a DataGrid on the Instances tab).
- Both are registered as `Application.Styles` (Semi's usage model — not merged resource dictionaries):
  ```xaml
  <Application.Styles>
      <semi:SemiTheme />
      <semi:DataGridSemiTheme />
  </Application.Styles>
  ```
- `Irihi.Avalonia.Shared` is pulled transitively.
- **No custom implicit styles / `ControlTemplate`s** for controls. Element sizes, spacing, radii, focus/selection states come from Semi defaults. A single optional `Styles/AppOverrides.axaml` is reserved *only* if a brand accent override is wanted (e.g. keep V2 cyan `#04DDF9` as the accent); otherwise omit it.

## 3. Palette & switching (D2)

- Semi ships Light/Dark palettes internally and follows Avalonia's `ThemeVariant`. Switching stays native:
  ```csharp
  public interface IThemeManager { void Apply(bool dark); }
  ```
  impl sets `Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light`.
- No V2 palette migration needed — the V2 brush keys (`WindowBackgroundBrush`, `TextBrush`, `AccentBrush`, …) are **not** carried over; XAML binds to controls and lets Semi style them. Layout XAML no longer sets theme brushes (except optional accent override).

## 4. Layout & styling (D3)

- **Keep V2 structure:** window grid rows — tab area / 5px splitter / log panel / status progress bar; three tabs (Instances, Link to torrent, Other); Instances tab = button row + DataGrid (Name / File count / Instance size); Link-to-torrent tab = field rows + action buttons + matched-files list; Other tab = status lines + Import DBInfo.xml + dark-mode checkbox; AddInstanceWindow rows (name, path+browse, copy/move radios, create, log, progress).
- **Styling is Semi's default** — no custom sizes, spacing, or templates. Where V2 used `{DynamicResource ...}` theme brushes on the window background / splitter / progress bar, v3 uses Semi defaults (or a minimal override brush if the accent decision above is made).
- Minimal polish allowed in layout XAML only: consistent margins/padding, `TextBox`/`Button` arrangement, column widths — matching V2 behavior, not restyling controls.

## 5. Theme switching & persistence

- Startup: read `AppSettings.IsDarkTheme`, call `themeManager.Apply(...)` in `App.OnFrameworkInitializationCompleted` **before** `MainWindow` shows (no flash).
- Toggle: `MainViewModel.IsDarkTheme` setter → `themeManager.Apply(value)` + `settingsStore.Save(AppSettings with preserved DataDirectory)` (`08` §7).
- Windows native title bar darkening (D4): small `Win32DarkTitleBar` helper (`DwmSetWindowAttribute` attr 20/19, `[SupportedOSPlatform("windows")]`) on `SourceInitialized` when dark; Linux no-op. **Verify at M5 whether Avalonia/Semi darkens the native title bar automatically; keep helper only if needed.**

## 6. Files summary

```
App.axaml                     # Application.Styles: semi:SemiTheme, semi:DataGridSemiTheme
Styles/ThemeManager.cs        # IThemeManager (App service)
Styles/AppOverrides.axaml     # OPTIONAL: brand accent override only
Views/MainWindow.axaml        # V2 layout structure, Semi-styled controls
Views/AddInstanceWindow.axaml
Views/FirstRunWindow.axaml
Controls/MessageDialog.axaml  # styled by Semi automatically
```

## 7. Testing (no render harnesses)

- **Build-time:** XAML compiles; `AvaloniaUseCompiledBindingsByDefault` catches binding/resource issues.
- **VM tests** (`App.Tests`): `IsDarkTheme` setter → `themeManager.Apply` called + `settingsStore.Save` preserves `DataDirectory` (mocked `IThemeManager`, `ISettingsStore`).
- **Manual QA at M5** (per AGENTS): user verifies light/dark on Windows + Linux, both dialogs, DataGrid, log, progress, title bar. No screenshot harnesses.

## 8. Decisions (locked)

- **D1** Semi.Avalonia 12.1.x (+ `Semi.Avalonia.DataGrid`) as the theme; no Fluent, no custom control templates.
- **D2** `ThemeVariant`/`RequestedThemeVariant` switching via `IThemeManager`.
- **D3** Keep V2 layout structure; use Semi default control styling (sizes, spacing) unchanged.
- **D4** Windows native title-bar dark via small DWM helper (Linux no-op); verify/remove-if-unneeded at M5.
- **D5** `IThemeManager` interface (App service) for testable dark-mode toggle.
- **D6** Package pins: `Semi.Avalonia` + `Semi.Avalonia.DataGrid` replace `Avalonia.Themes.Fluent` (and the draft SimpleTheme).
