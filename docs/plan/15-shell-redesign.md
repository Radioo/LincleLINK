# 15 - Shell redesign (M9)

Status: **Approved / locked**. Expansion of `00-high-level.md` §14; follows M8 (`14-ux-clarity.md`).
Implements "Concept A - sidebar + inspector" from the approved layout proposal.

The tab strip becomes a modern desktop shell: left sidebar navigation with a permanent
storage card, page-based content, a Library master-detail with an inspector panel, a
slide-over add flow, a vertical torrent wizard, card-based settings, and a single
bottom activity bar that owns all feedback. App-layer only, plus two small Core
additions (D2, D3). Vocabulary and behavior from plan 14 are unchanged.

## 1. Shell (D1)

- `MainWindow` becomes: sidebar (200 px) | page area, with a log drawer and an
  activity bar docked under both.
- Sidebar: nav list (Library / Torrent pre-fill / Settings; glyph + label,
  `SelectedNavIndex` on `MainViewModel`) and, pinned at the bottom, the **storage
  card**: "Saving {X}" headline, a thin storage-vs-library bar, and Storage /
  Library / Free space rows. Visible from every page.
- Pages switch by `IsVisible` on three panels bound to nav flags (no new page VMs;
  the existing `MainViewModel` + `TorrentCheckViewModel` split already carries the
  state). A later milestone may promote pages to their own VMs; out of scope here.
- Theme-variant brushes (`LLSidebar`, `LLCard`, `LLHairline`, `LLAccentSoft`,
  `LLDanger`, `LLWarning`, `LLSuccess`, `LLVeil`, `LLSurface`) are defined in
  `App.axaml` ThemeDictionaries - deliberately **not** Semi's internal color keys,
  whose names are unverifiable at build time.

## 2. Library page (D2)

- Header: title + entry count, filter box (`FilterText` filters the grid,
  case-insensitive substring on the name), primary "Add folder…" button.
- Master-detail: the DataGrid plus an **inspector** for the selected entry -
  Files / Size when deployed / **Unique to this entry**, and the Deploy / Export /
  Remove buttons (replacing the global button row; the row context menu stays).
- **D2 (Core):** `IInstanceRepository.GetUniqueSizeAsync(name)` - total bytes of
  hashes referenced by this entry and no other. SQLite: one whole-table
  `GROUP BY HashedFileName HAVING COUNT(DISTINCT InstanceName)=1` raw query (same
  cost class as the unused-files scan); JSON: in-memory equivalent. Loaded lazily
  per selection; shows "…" while computing.
- Empty state gains the three-node model diagram (folder → storage → deployed).

## 3. Slide-over add flow (D4)

- "Add folder…" opens the existing `AddInstanceViewModel`/`AddInstanceWindow`
  **in-window**: a veil over the page area plus a right-docked 380 px surface
  hosting the view via the ViewLocator (`ContentControl`). No more separate
  dialog window for adding (error/confirm dialogs remain windows via
  `IDialogService`).
- The view gains a panel header (title + ✕ close, `CloseCommand`, disabled while
  busy). `CompletedSuccessfully` on the VM lets the shell report the outcome and
  refresh on close. `MainViewModel` drops its `IAppDialogHost` dependency.

## 4. Activity bar + log drawer (D5)

Replaces the plan-14 bottom stack (status line / progress / expander). One bar,
three states, identical on every page:

- **Idle:** last outcome line (`LastOutcome`, e.g. "✓ Deployed 48,213 files").
- **Running:** transient status text + compact progress + percent + **Cancel**.
- **Finished with issues:** the outcome line renders in the warning color
  (`LastOutcomeIsWarning`), e.g. "⚠ Deployed 4,812 files; 3 failed - see log".

"Activity log ▸" toggles a 160 px drawer above the bar containing the full log.
`IOperationHost` gains `ReportOutcome(message, isWarning)` so feature VMs (torrent)
can post outcomes; `MainViewModel` posts them for its own operations from the
result records (linked/failed/exported/deleted counts).

## 5. Torrent page as a vertical stepper (D6)

- Inputs consolidate into one card (2×2: entry, torrent file, relative path,
  download folder), followed by three connected step nodes (✓ when done).
- Step summaries/hints from plan 14 render beside each step; the matched-file list
  moves behind an expander.
- The step-3 button states its exact effect: **"Link {EligibleCount} files"**,
  where `EligibleCount` = verified files with no bad piece (computed after
  Verify pieces; reset with the gates).

## 6. Settings as cards (D7)

2-column card grid: Appearance (theme radios), Performance (worker threads),
Data location (path + change + restart note), Import from v1. Same bindings.

## 7. Out of scope / deferred

- Per-row hover action buttons (context menu + inspector cover it; hover buttons
  in Avalonia DataGrid rows are deferred until the shell settles).
- Relative timestamps in the activity bar; entry history; integrity page.
- Sidebar collapse-to-icons below ~800 px (window min-width instead).
- Animated slide-in transition - added only if it survives manual testing.
