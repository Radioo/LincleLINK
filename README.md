# LincleLINK

LincleLINK stores game data **deduplicated**: you add folders to your library,
identical files across versions are stored only once, and any entry can be
recreated ("deployed") anywhere on the same drive using hard links - at no extra
disk cost. Adding several versions of the same game costs you roughly the size
of one, plus what's unique to each.

<img width="1120" height="700" alt="image" src="https://github.com/user-attachments/assets/832ce7fa-680b-45df-9607-a1707a04427c" />

## Installation

Grab the build for your platform from the [releases page](https://github.com/Radioo/LincleLINK/releases):

- **Windows** - `LincleLINK-win-x64.exe`, portable, just run it.
- **Linux** - `LincleLINK-linux-x64.flatpak` (recommended): install with `flatpak install <file>`. A portable `.tar.gz` is also available.
- **macOS** - `LincleLINK-osx-{x64,arm64}.app.zip`, unzip and drop the `.app` into Applications.

On first launch, choose where LincleLINK keeps its storage. Pick a drive with
room for your data - deploying only works onto that same drive.

## Usage

### Add a folder to the library
**Library → Add folder…** Pick a name and the folder (for games, use the `data`
folder), then choose what happens to the originals:

- **Reclaim space** (recommended) - duplicates are absorbed into storage and the
  folder's files become hard links to it. The folder keeps working exactly as
  before, and the duplicate space is freed. Requires the folder and storage on
  the same drive (checked automatically).
- **Keep originals untouched** - files are copied into storage; the folder is
  not modified.

### Deploy an entry
Select an entry and click **Deploy to folder…** - the entry is recreated at the
chosen location with its original names and folder structure, via hard links,
using no extra disk space. If files already exist there, you choose to replace
them, skip them, or cancel.

### Pre-fill a torrent download
Already have most of a torrent's files in your library? **Torrent pre-fill**
links them into the download folder so your client only fetches what's missing:

1. **Match files** - quick name/size comparison against the chosen entry.
2. **Verify pieces** - byte-exact check; only fully verified files are eligible,
   so your client won't overwrite them.
3. **Link verified files** - then point your torrent client at the download
   folder.

### Clean up storage
**Clean up storage…** (in the sidebar's storage card) finds files that no longer
belong to any library entry, shows how much space they take, and deletes them
after confirmation. Run it after removing entries to actually free the space.

## Good to know

- **Removing an entry never deletes files** - it only forgets the index; the
  files stay in storage until a cleanup.
- **Hard links share their bytes.** If you want to edit or replace a deployed
  file, delete it first and put the new file in its place - editing it in place
  would modify the copy in storage (and every other deployment of it).
- **Everything lives on one drive.** Storage, your added folders (in Reclaim
  mode), and deploy targets must share a drive/partition; the app checks this up
  front and tells you when they don't.
- The selected entry's **"Unique to this entry"** figure shows what removing it
  (plus a cleanup) would actually free - everything else is shared with other
  entries.
