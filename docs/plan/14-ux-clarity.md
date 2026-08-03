# 14 — UX clarity (M8)

Status: **Approved / locked**. Expansion of `00-high-level.md` §14; follows M7 (`13-sqlite-storage.md`).

Users consistently fail to form the right mental model: "instance" reads as "a separate
copy of the game" (the opposite of the point), and "Copy files / Move files" answers a
question users don't know they're being asked. This milestone renames the user-facing
vocabulary, redesigns the add flow, surfaces operation results, and closes two real
defect edges discovered during the analysis (cross-volume move data-loss window,
`Win32HardLinker` errno mapping). **On-disk data, schema, and Core domain type names are
unchanged** — `Instance`, `db/`, `linclelink.db`, JSON/SQLite layouts all stay
byte-identical (plan 02/04/13 constraints hold). Only user-visible language and App/Core
service behavior change.

## 1. Vocabulary (D1)

One metaphor: **your Library, backed by Storage, deployed via links.**

| v2/v3 term | M8 term | Notes |
|---|---|---|
| Instance (UI) | **Entry** in the **Library** | Domain type stays `Instance`; `Instances` tab → `Library` |
| `db` folder (UI) | **Storage** | On-disk name `db/` unchanged |
| Add instance | Add folder to library… | |
| Copy files | Keep originals | Radio card with description |
| Move files | Reclaim space (recommended) | Radio card with description; see D3 |
| Link files | Deploy to folder… | |
| Copy hashed files | Export storage files… | |
| Check for unused files | Clean up storage… | |
| Delete instance | Remove from library | |
| Link to torrent (tab) | Torrent pre-fill | In-tab heading: "Pre-fill a torrent download" |
| Other (tab) | Settings | Status readouts move to the Library tab header |
| Add instance hash threads | Worker threads | Also governs cleanup parallelism |

Core services emit user-visible strings (log lines, dialog bodies); those adopt the new
vocabulary too. `LogMessages` constants are renamed accordingly.

## 2. Add flow (D2, D3)

- The add dialog becomes "Add folder to library": Name, Folder, then two description
  radio cards — **Reclaim space (recommended, default)** and **Keep originals
  untouched** — each stating exactly what happens to the user's folder and showing an
  estimated size (background folder-size scan, "Calculating…" while pending).
- **D2 — same-volume pre-flight.** New port `IHardLinkPreflight.CheckLinkTo(directory)`
  (Infrastructure impl: probe file in `db/`, hard-link attempt into the target
  directory, both deleted immediately; inconclusive probes return success so real
  operations surface their own errors). Consumed by:
  - the add dialog: cross-volume folder → Reclaim card disabled with an inline
    explanation, selection falls back to Keep originals;
  - `InstanceService` (Move mode): fails fast with a user-presentable error (belt and
    suspenders under the UI gate);
  - `LinkingService.LinkInstanceAsync` and the torrent link step: one clear error
    dialog/log line up front instead of N per-file failures.
- **D3 — safe reclaim ordering.** Move mode no longer deletes the original before
  linking. New order: hard-link the store copy to a temp name beside the original, then
  atomically replace the original with the temp link (`IFileSystem.MoveFile` with
  overwrite). A failed link leaves the original untouched. This supersedes the
  delete-then-link order shipped in M2 (register `12-verification.md` §6 entry stands;
  `05-instance-flow.md` D3 note updated).
- `Win32HardLinker`: error 17 is `ERROR_NOT_SAME_DEVICE`, not "already exists"
  (that is 80/183). Remapped; cross-drive failures now say so on both platforms.

## 3. Feedback (D4, D5)

- **D4 — results are reported, not discarded.** Every operation ends with a summary log
  line built from its result record (deployed/failed counts + capped per-file error
  details, export copied/existed counts, torrent linked/skipped counts, cleanup single
  summary). Declined confirmations log a cancellation line.
- **D5 — status channel + log demotion.** High-frequency per-file lines (hashing,
  storing, export skips, cleanup counter) move from the log to a transient one-line
  status channel (`IProgress<string> status` parameter, latest-wins in the UI). The
  main-window log collapses into a "Details" expander under a visible status line;
  the progress bar gains a **Cancel** button (a real `CancellationTokenSource` finally
  flows into the service `ct` parameters). `IOperationHost` passes an
  `OperationContext(Log, Status, Percent, CancellationToken)`.
- Duplicate files on deploy become a three-way choice — **Replace / Skip existing /
  Cancel** — via a new `IDialogService.AskConflictAsync` (third dialog button);
  `LinkResult` gains a `SkippedExisting` count.

## 4. Shell (D6, D7)

- **D6 — Library tab**: storage status header (storage size / you are saving / free
  space) moves here from Settings; empty state with the product pitch and an
  "Add folder to library…" CTA replaces the bare grid; row context menu (Deploy,
  Export, Remove); tooltips on all action buttons. Columns: Name / Files / Size.
- **D7 — Torrent tab as a visible wizard**: purpose blurb, three numbered steps
  (Match files → Verify pieces → Link verified files) with inline per-step results
  ("2,113 of 2,480 files matched") and lock hints ("Match files first."). Gate
  properties renamed to describe their own state (`FilesMatched`, `PiecesVerified`);
  editing the download path no longer resets piece verification (it doesn't depend on
  it). Linked/skipped counts surface.
- First-run dialog explains storage instead of naming `db/`/`instance/` folders.

## 5. Out of scope

- No new operations, no schema changes, no README feature changes beyond terminology.
- Window title, branding, and theming are unchanged.
- `Instance`/`db` naming inside code, tests, and persistence stays as-is (plan 02/13).
