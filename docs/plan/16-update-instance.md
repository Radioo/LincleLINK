# 16 - Update, Duplicate and the file browser (M10)

Status: **Built**, steps 1 to 8 of section 13. Differences from this text as built are
listed in section 14. Vocabulary is defined in `CONTEXT.md`
(Instance, Storage, Deploy, Update, Source, Destination, Removal, Pending changes,
Duplicate). The path rule is recorded in `docs/adr/0001-case-insensitive-instance-paths.md`.

The workflow this serves: a new game version comes out, the user duplicates the
latest entry, drops the update files on the copy, and deploys the result.

## 1. Rules that hold everywhere (D1)

- An Update, a Removal and a Duplicate write Instance rows in SQLite and may add new
  files to Storage. They never modify or delete a Storage file and never touch a
  deployed folder. Deployed folders are hard links to immutable Storage files, so
  this holds by construction. The app does not track deployments.
- All three run through the existing one-operation-at-a-time gate (`IsBusy` /
  `RunOperationAsync` pattern), so they cannot overlap a Deploy or a storage cleanup.
- Update adds and replaces. It never removes a path. Removal happens only in the
  file browser.
- Archives get no special treatment. A dropped `update.7z` is one file.
- Copy mode only. Update never replaces the user's dropped files with links.

## 2. Entry points (D2)

- Context menu and inspector both gain "Update...", "Duplicate..." and
  "Browse files...".
- The library list and cards accept no drops. The only drop zone is inside the
  dialog.

## 3. Duplicate (D3)

- One dialog with a name field, prefilled `<name> - copy`, checked by
  `InstanceNameValidator` and the same uniqueness rule as Add.
- Copies files, directories, detected game and custom logo. Copies no Storage content.
- The new entry is selected when the dialog closes.
- Core: a `DuplicateAsync(sourceName, newName)` use case on top of the repository.

## 4. One dialog for browsing and updating (D4)

"Browse files..." opens the dialog with nothing staged. "Update..." opens the same
dialog with the drop zone showing. It needs far more width than the 380 px add
slide-over, so it is its own in-window surface.

Layout:

- Source list and drop zone. Two buttons under the zone: "Add folder..." and
  "Add files..." (multi-select, one new `IDialogService` method).
- Summary line: files added, replaced, identical and skipped, size change of the
  entry, bytes new to Storage, files and bytes pending Removal, and the detected
  version change when there is one.
- The tree (section 6), a filter box and a "changes only" toggle.
- Footer with Apply and Cancel, progress bar, status line and log while applying,
  following `AddInstanceViewModel`. The dialog closes on success.

## 5. Sources and destinations (D5)

- A drop or a pick becomes a Source. Several Sources can be staged. The list shows
  them in order, each with a remove button and its Destination, for example
  `bm2dx.dll -> modules/`.
- Destination defaults to the Instance root. Dropping onto a folder row in the tree
  stages that drop with the folder as its Destination. The source row lets the user
  change it.
- A Source that is a single folder counts as its Destination, so its contents merge
  into it. A per-source toggle "keep the dropped folder as a subfolder" gives the
  Explorer behaviour. Several items dropped together land in the Destination under
  their own names.
- When two Sources hold the same path the later one wins and the tree marks the
  conflict.
- Any node from a Source can be excluded in the tree.
- Empty folders in a Source are added, same as Add.

Enumeration for Sources is separate from Add's:

- It does not descend into directory symlinks or junctions. Each one shows in the
  preview as "skipped, link to <target>".
- File symlinks are read through as ordinary files.
- An unreadable folder or file becomes an error row with the OS message, is
  excluded, and does not stop the scan or block Apply.

Known issue left alone here: Add uses `Directory.GetFiles(..., AllDirectories)`,
which follows directory links and can loop, and aborts on the first unreadable folder.

## 6. Preview tree (D6)

One tree of the Instance as it will look after Apply.

- Badges for added and replaced files. Unchanged files are dimmed. Pending Removals
  are struck through. Excluded source nodes are shown unticked.
- Folders show a count of changes inside them. On open, only paths that contain
  changes start expanded.
- A replaced file shows old and new size on hover.
- The path-only tree appears as soon as enumeration finishes. Hashing then fills in
  the exact status per file: added, replaced, or identical. Apply is enabled when
  hashing completes.
- Rows show name, size and folder totals, folders sorted first. Row context menu has
  "Reveal in Storage". No uniqueness marker and no per-file export.
- Blocking error: a file and a folder claiming the same path, in either direction.
  The node is red, the summary names the collision, and Apply stays disabled until
  the user removes the Source or excludes the node.

## 7. Removal (D7)

- Select files or folders, multi-select allowed, and remove. The nodes stage as
  struck through and nothing changes until Apply.
- The footer says how many files and bytes leave the entry, that the content stays
  in Storage until the next storage cleanup, and that deployed folders keep it.
- A folder stays as an empty folder when all its files are removed. It goes away
  only when the folder node itself is removed.

## 8. Path matching (D8)

Paths match case-insensitively on every platform. The Instance's existing casing
wins for directories and replaced files, and new paths keep the casing they arrive
with. Add and Duplicate follow the same rule, so an Instance never holds two paths
that differ only by case. See ADR 0001.

## 9. Apply (D9)

1. Low disk check against the exact bytes new to Storage, known from hashing.
2. Copy new content into Storage, dedup by hash as Add does.
3. Rerun game detection if the Pending changes touch a file the detector reads
   (section 10).
4. Write the new file list, directory list, totals and detection in one database
   transaction.

Cancel or failure before step 4 leaves the Instance exactly as it was. Content
already copied stays in Storage unreferenced and the existing cleanup removes it.

## 10. Game detection over an Instance (D10)

Amended while building step 8: the add flow recommends adding only a game's data
folder, so the files a game is identified by (`prop/ea3-config.xml`, the game DLL)
usually are not inside an Instance. Detection over the Instance alone would find
nothing for those entries.

- A read-only `IFileSystem` view (`PlannedInstanceFileSystem`) maps Instance paths to
  their Storage files, and to the Source's file on disk for content a Source brings,
  so `IGameVersionDetector` can run against an Instance plus its Pending changes.
- Order: an Instance that holds identity files speaks for itself and is detected
  through that view. If it holds none, or only a game DLL without a date code, the
  normal detector runs on each dropped folder on disk, latest Source first, with the
  same walk-up Add does. A result from a dropped folder counts only when its game
  code matches the entry's current game, or the entry has none.
- Only Sources that change the entry are asked. Loose dropped files give no folder to
  detect from, because the detector also looks into a start folder's subfolders, and
  for a file picked out of Downloads those are unrelated neighbours.
- Changes that touch no identity file and bring no Source detect nothing. Finding
  nothing never clears a tag, and a result without a date code never replaces a tag
  that has one.
- The preview shows the outcome before Apply, for example
  "Detected version: 2026031800 -> 2026091700", with "(found next to the dropped
  folder)" when that is where it came from. A checkbox next to it is ticked by
  default; unticked, the entry keeps its tag. The tick resets when a different
  version is detected.
- Apply waits for the detection of the current plan and saves exactly the version
  shown. It does not detect again.
- A custom logo stays as it is.

## 11. Tree control (D11)

A flattened list of visible rows in the built-in virtualized `ListBox`. Each row
carries its depth for indentation. We write the visible-rows collection, expand
and collapse, and Left/Right key handling. Selection, context menu, paging keys
and Semi styling come from `ListBox`.

Why not the alternatives, as researched in September 2026:

- Built-in `TreeView` does not virtualize in Avalonia 12.1.
- `Avalonia.Controls.TreeDataGrid` 12.x needs a paid Accelerate Pro license, and
  the MIT 11.x package crashes on Avalonia 12.
- The MIT fork `TreeDataGrid.Avalonia` 12.0.0 has one maintainer, no Semi theme and
  no evidence at our size. It is the fallback.
- `ProDataGrid` replaces the `Avalonia.Controls.DataGrid` assembly the library page
  uses and collides with `Semi.Avalonia.DataGrid`.
- Eremex TreeList is closed source, needs its own theme and pulls heavy dependencies.

Risk to retire first: expanding a folder with tens of thousands of children must
raise one range change or a reset, never one event per row. Build that path first
and time it against a synthetic instance of 150k files before building the rest of
the dialog.

## 12. Core shape and tests (D12)

- The plan computation is a pure function in Core: Instance + Sources (with hashes,
  Destinations, exclusions) + Removals in, a list of resulting entries with their
  status and any blocking errors out. It owns the case rule, later-source-wins, the
  single-folder rule and collision detection. Unit tests cover it directly.
- The tree view model (flattening, expand and collapse, change counts, filter) is
  plain C# and tested in `LincleLINK.App.Tests`.
- Apply, Duplicate and the Storage-backed `IFileSystem` view get service tests in
  `LincleLINK.Core.Tests` next to the existing `InstanceService` tests.
- No UI automation.
- No schema migration is needed. The existing file and directory tables hold
  everything.

## 13. Build order

1. Tree view model and `ListBox` rows with the 150k timing check (D11).
2. Read-only browser dialog with filter and "Reveal in Storage".
3. Removal staging and Apply.
4. Duplicate.
5. Plan computation, Source enumeration and hashing, preview badges and summary.
6. Apply for Updates, low disk check.
7. Destinations, drop onto folder rows, exclusions.
8. Detection over an Instance.

## 14. As built: differences from the text above

- Exact statuses (replaced versus identical) fill in when a Source finishes hashing,
  not file by file. Reloading a large tree per file would reset the list's scroll
  position over and over. Until then a file at an existing path shows "checking".
- Skipped links and unreadable files or folders are listed under their Source in the
  source list, not as rows in the tree.
- "Remove from entry" on a path that a Source also fills leaves the Source's content
  out too. In the planner a Source wins over a Removal, so the removal alone would
  show nothing. "Restore" takes both back.
- Apply refuses a Source file whose size or write time changed after it was hashed,
  because content copied under a stale hash would sit in Storage under the wrong name.
  It checks twice: all files before anything is asked or copied, and each file again
  right before its own copy, since the low disk question and the earlier copies can
  take minutes. A mismatch in the second check saves nothing; what was copied so far
  stays in Storage unreferenced until the next storage cleanup.
- "Reveal in Storage" on an added or replaced file opens the Storage folder, because
  its content is not in Storage before Apply.
- Apply and the Destination box refuse rooted and `..` paths, which Deploy would
  reject later.
- The whole dialog takes drops, and the Delete key stages the selection. A drop on a
  file row lands in that file's folder.
- A folder of a legacy entry that is missing from its directory list disappears from
  the preview once all its files are removed. Deploy never created such a folder
  either, so the preview is accurate.
- Still open from section 1: both dialogs keep their own busy flag behind the modal
  veil, as Add does, instead of setting the shell's `IsBusy`. From section 8: Add
  still collects directories case-sensitively, which only matters on Linux and macOS.
- Found on the way and fixed since: `FileStore` wrote straight to the final hash name,
  so a cancelled copy left a partial file that later adds would trust. Copies now
  stream into an `incoming-<guid>.lincletmp` file and take their name only when
  complete. Section 9's "content already copied stays in Storage" still holds for
  whole files; a copy that was cut off leaves nothing.
- Section 9 step 3 is gone: detection runs for the preview, not inside Apply, and
  Apply saves the version the user saw and accepted (section 10).
- Game codes are compared literally. An entry tagged `LDJ` does not take a version
  from a dropped folder that says `TDJ`, although both are the same game series.
