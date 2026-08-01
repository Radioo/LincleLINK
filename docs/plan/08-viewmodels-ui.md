# 08 — ViewModels & UI (Avalonia)

**Parent:** [00-high-level.md](00-high-level.md) §4, §5, §9
**Milestone:** M2/M4 (VM shells), M5 (polish)
**Status:** Approved

## 1. Scope

Define the Avalonia presentation layer: views, view models (CommunityToolkit.Mvvm), `IDialogService` adapter, command gating, logging, progress, and the first-run window. All application logic stays in Core services (05/06/07); VMs are thin binding + orchestration shells.

## 2. Files (deltas vs `01`)

```
src/LincleLINK.App/
├─ ViewModels/
│  ├─ Base/ViewModelBase.cs            # ObservableObject
│  ├─ MainViewModel.cs                 # shell: 3 tabs
│  ├─ AddInstanceViewModel.cs
│  └─ FirstRunViewModel.cs             # first-launch data-dir prompt (03 §5)
├─ Views/
│  ├─ MainWindow.axaml / .cs           # shell
│  ├─ AddInstanceWindow.axaml / .cs    # modal
│  └─ FirstRunWindow.axaml / .cs
├─ Services/
│  ├─ DialogService.cs                 # IDialogService impl
│  └─ ViewHostWindow.cs                # hosts any VM's View (ViewLocator) in a Window
└─ Controls/
   └─ MessageDialog.axaml              # styled OK / YesNo message box (Confirm/Info/Error)
```

**Core addition (D4):** `Application/StatusService.cs` + `StatusSummary` record — computes the Other-tab summary (db size, savings, free space) from `IFileStore` + `IInstanceRepository` + `IDriveInfoProvider` (V2 `UpdateDBSize` logic, testable).

**Test project addition (D6):** `tests/LincleLINK.App.Tests` for VM logic (no rendering — plain xUnit + CommunityToolkit.Mvvm work headless; consistent with AGENTS "no render-capture harnesses").

## 3. Composition & startup ordering

`App.axaml.cs` `OnFrameworkInitializationCompleted` (Avalonia 12, per `01`):

1. Build a **bootstrap** `ServiceProvider` (Core + settings + dialog infra, `IAppPaths` defaulting to CWD).
2. `FirstLaunchService.Resolve()` (`03` §5):
   - `UseExistingSettings` / `AdoptCurrentDirectory` → continue with resolved dir;
   - `PromptForDirectory` → host `FirstRunWindow` (via `ViewHostWindow`), get chosen dir, `settingsStore.Save`.
3. Build the **main** `ServiceProvider` with `IAppPaths` constructed from the resolved `DataDirectory`.
4. `desktop.MainWindow = new MainWindow { DataContext = provider.GetRequiredService<MainViewModel>() }`.

`IAppPaths` therefore depends on a resolved dir, not on construction order; the two-phase bootstrap keeps services single-instance and avoids re-resolution hacks.

## 4. `MainViewModel` (shell)

**Observable state** (CommunityToolkit `[ObservableProperty]`):

- `ObservableCollection<InstanceListEntry> Instances` + `InstanceListEntry? SelectedInstance`
- `ObservableCollection<string> LogLines` (auto-scroll behavior)
- `double Progress` (0–100), `bool IsBusy`
- Torrent tab: `string TorrentFilePath`, `string RelativePath`, `string TorrentDownloadPath`, `ObservableCollection<string> MatchedFiles`
- Other tab: `string DBSize`, `string Savings`, `string FreeSpace`, `bool IsDarkTheme`/`bool IsLightTheme` (mutually exclusive radio buttons), `int ThreadCount` (add-instance hash workers, `1..MaxThreadCount`)
- Derived gates: `bool CanCheckPieces` (last CheckFiles `Matched > 0`), `bool CanLinkTorrent` (last CheckPieces ok && `TorrentDownloadPath` non-empty)

**Commands** (`[RelayCommand]`/`[AsyncRelayCommand]`, with `CanExecute` + `NotifyCanExecuteChangedFor`):

| Command | CanExecute |
|---|---|
| OpenAddInstance | `!IsBusy` |
| LinkInstance / CopyHashed / DeleteInstance | `!IsBusy && SelectedInstance != null` |
| CheckUnused / ImportLegacy | `!IsBusy` |
| CheckFiles | `!IsBusy && SelectedInstance != null && TorrentFilePath` non-empty |
| CheckPieces | `!IsBusy && CanCheckPieces` |
| LinkToTorrent | `!IsBusy && CanLinkTorrent` |
| BrowseTorrentFile / BrowseTorrentDlPath | `!IsBusy` |

**Operation pattern (replaces V2 `async void` + `IsFree` + `UIContext`):**

```
IsBusy = true
try:
    result = await service.OpAsync(request, progressLog, progressPercent, ct)
    apply result → update gates, MatchedFiles, StatusSummary, LogLines
    if result.Error != null → dialogService.Error(...)
catch OperationCanceledException → log "cancelled"
catch InstanceStorageException/IOException → dialogService.Error(...)
finally: IsBusy = false; RefreshStatus()
```

- **`IProgress<T>` marshals to the UI thread** via the captured `SynchronizationContext` (D2) — V2's manual `UIContext.Send` pattern is gone. `AsyncRelayCommand` resumes on the UI context, so `ObservableCollection` mutations are safe. High-frequency add-instance log lines go through `BatchedLog` (queue + `Dispatcher.UIThread.Post` at `Background` priority, ~100 lines per batch). Both log panels are virtualizing `ListBox`es (`AutoScrollBehavior` pinned to the last item), so a huge instance keeps every line without flooding the UI thread or unbounded TextBlock/GC cost (D7).
- **Add-instance dialog (D5):** `OpenAddInstance` resolves `AddInstanceViewModel` from DI, forwards `ThreadCount` from the Other-tab slider, shows it via `DialogService.ShowDialogAsync(vm)`, and on close refreshes `Instances` + status (V2 did this after `ShowDialog`).

**Startup:** the initial `RefreshInstancesAsync` + `RefreshStatusAsync` run from `MainWindow.OnOpened` (fires on the UI thread once the window is shown and the dispatcher pumps), not from `OnFrameworkInitializationCompleted` — which runs before the window is shown/bound. First-run (window shown early for the bootstrap dialog) is covered by App firing the refresh directly once the `DataContext` is set while the window is already visible (D8).

## 5. `AddInstanceViewModel`

- `string InstanceName`, `string DataPath`, `CopyMoveMode Mode` (two `RadioButton`s → `IsCopyChecked`/`IsMoveChecked` mapped to `Mode`), `bool IsBusy`, `ObservableCollection<string> LogLines`, `double Progress`
- `MakeInstanceCommand` (`[AsyncRelayCommand]`): `instanceService.CreateInstanceAsync(request, log, percent, ct)` → on failure, `dialogService.Error(result.Error)`; keeps window open. Close happens from the hosting code when `result.Success`.
- `BrowseCommand`: `dialogService.PickFolder(...)`.
- No validation logic here — all in `InstanceService` (`05`).

## 6. `IDialogService` — Avalonia adapter

```csharp
public interface IDialogService
{
    bool Confirm(string message, string title = "");
    void Info(string message, string title = "");
    void Error(string message, string title = "");
    string? PickFolder(string title);                 // StorageProvider, null = cancelled
    string? PickOpenFile(string title, string filter); // .torrent / DBInfo.xml
    Task<bool?> ShowDialogAsync(ViewModelBase vm);    // hosts any VM's View in a Window
}
```

- `DialogService` captures the owner `Window` (set from `MainWindow` on load) and calls pickers via `TopLevel.StorageProvider` on the UI thread.
- `MessageDialog` is a styled `UserControl` (message + OK / YesNo buttons), reused by `Confirm`/`Info`/`Error`; avoids any WinForms.
- `ShowDialogAsync` uses `ViewHostWindow` + the app `ViewLocator` (VM → View by name convention, `01`) — the add-instance and first-run windows are driven this way, so VMs never reference `Window` types.

## 7. Theming & thread-count binding (detail in `09`)

`IsDarkTheme`/`IsLightTheme` (mutually exclusive `RadioButton`s on the Other tab, matching the first-run window): the dark setter → `ThemeManager.ApplyTheme(value)` + `settingsStore.Save` preserving `DataDirectory` + `HashThreadCount`. `ThreadCount` (Other-tab slider, `1..Environment.ProcessorCount`) persists via `settingsStore.Save` preserving the theme + `DataDirectory`; read initial values from settings at startup (seeded in `App` before the window shows, no flash).

## 8. Logging

- `LogLines` is the single panel. Services report through `IProgress<string>` (delegated from the VM); the VM appends its own lines (e.g. "Selected X", operation summaries from results).
- Auto-scroll-to-bottom via a small attached behavior on the `ScrollViewer` (no `MainWindowControls` struct — V2's control-passing pattern is replaced by data binding, `01`).
- First-run window appends nothing (no panel).

## 9. Edge cases

- Commands remain gated while a dialog window is open (`IsBusy` set for the modal lifetime).
- `SelectedInstance` cleared when its manifest is deleted → gates update via `NotifyCanExecuteChangedFor`.
- Torrent `CanCheckPieces`/`CanLinkTorrent` reset to `false` when inputs change (setter of `TorrentFilePath`/`RelativePath`/`TorrentDownloadPath` resets gates, matching V2's intent that results are stale after edits).
- Empty `Instances` → grid shows empty state text.

## 10. Test plan (`tests/LincleLINK.App.Tests/` — logic only, no rendering)

**`MainViewModelTests`** (mocked services via NSubstitute):
- initial load populates `Instances` + `StatusSummary`;
- command gating matrix: `IsBusy` disables all; `SelectedInstance == null` disables Link/Copy/Delete/CheckFiles; gates derived from last results;
- CheckFiles sets `MatchedFiles` + `CanCheckPieces`; CheckPieces failure leaves `CanLinkTorrent` false; LinkToTorrent success resets gates;
- input edits reset `CanCheckPieces`/`CanLinkTorrent`;
- `IsDarkTheme` setter → `settingsStore.Save` called with preserved `DataDirectory`;
- add-instance dialog: `ShowDialogAsync` invoked; `Instances` refreshed on close.

**`AddInstanceViewModelTests`**: `MakeInstance` maps `Mode` from radio state; error result → `dialogService.Error`, window stays open; success → close signal.

**`StatusServiceTests`** (Core): db size = sum of `db/` lengths; savings = sum(instance totals) − db size; free space via `IDriveInfoProvider` (mock).

## 11. Decisions (locked)

- **D1** `DialogService` hosts any VM via ViewLocator + `ViewHostWindow`; `MessageDialog` for Confirm/Info/Error; pickers via `StorageProvider`.
- **D2** `IProgress<T>` UI-thread marshaling via captured `SynchronizationContext`; V2 `UIContext.Send` removed.
- **D3** Command gating = global `IsBusy` + per-command `CanExecute`; results derive `CanCheckPieces`/`CanLinkTorrent`.
- **D4** Add `StatusService` in Core for the Other-tab summary.
- **D5** Add-instance opens through `DialogService.ShowDialogAsync`; list refreshed on close; `ThreadCount` forwarded to the dialog VM.
- **D6** Add `tests/LincleLINK.App.Tests` (logic-only VM tests; no render harnesses).
- **D7** `BatchedLog` queues high-frequency log lines and drains them to the UI in bounded batches at `Background` priority (fallback: synchronous in headless tests); the log panels are virtualizing `ListBox`es that keep all lines.
- **D8** Initial refresh fires from `MainWindow.OnOpened` (plus an `IsVisible` fallback in `App` for the first-run path).
