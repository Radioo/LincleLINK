# LincleLINK

> User guide for the LincleLINK workflow. Architecture, data layout and
> the rewrite plan live in [`docs/plan/`](docs/plan/). Since v3 M8 the UI uses the
> library/storage vocabulary described below; the on-disk layout is unchanged and
> fully compatible with v2 data.

# Installation

Grab the build for your platform from the [releases page](https://github.com/Radioo/LincleLINK/releases):

- **Windows** — `LincleLINK-win-x64.exe`, portable, just run it.
- **Linux / Steam Deck** — `LincleLINK-linux-x64.flatpak` is recommended: install it with `flatpak install <file>` (on the Deck, open it in Desktop Mode and Discover handles it). The Flatpak registers the app with your desktop, so extras like taskbar progress work out of the box. The `.tar.gz` is a portable alternative; install its bundled `.desktop` file and icon manually if you want desktop integration.
- **macOS** — `LincleLINK-osx-{x64,arm64}.app.zip`, unzip and drop the `.app` into Applications.

# How does it work?

LincleLINK is a deduplicated store for game data. You add folders to your
**library**; identical files across versions are stored only once in **storage**;
and any library entry can be **deployed** to any folder on the same drive via hard
links, using no extra disk space.

## Storage
All data lives in the app's own `db` folder ("storage" in the UI). Files are
renamed to their MD5 hash, which ensures only unique files are ever stored.

## The library
A library entry is just an index: file names, their hashes, and their location
within the original folder structure — not a separate copy of the data. Removing
an entry never deletes files from storage.
***It is highly recommended to only add the `data` folder of a game as an entry.***

When adding a folder, you choose what happens to the original files:

- **Reclaim space (recommended)** — duplicates are absorbed into storage and the
  folder's files become hard links to it. The folder keeps working exactly as
  before and the duplicate space is freed. Requires the folder and storage on the
  same drive (the app checks this up front).
- **Keep originals untouched** — files are copied into storage; the folder is not
  modified. New files use extra disk space.

Since v3 (M7), entry records are stored in a SQLite database (`linclelink.db`)
instead of `instance/*.json`. Existing JSON data is migrated automatically with a
one-time prompt at launch; the old JSON files are deleted once the migration
completes successfully. Here's an example of one file record:
```
    {
      "FileName": "25063_pre.2dx",
      "RelativePath": "sound\\25063",
      "FileSize": 463806,
      "HashedFileName": "7AFE6AC1B80128D44BA5357D4349B21A.2dx"
    },
```

## Deploying
Once you have at least one entry, **Deploy to folder…** recreates it at a
destination of your choice using hard links, keeping the original file names and
directory structure. ***Hard links only work on the same drive/partition — the app
verifies this before deploying. If you wish to edit/replace a deployed file,
delete it first! Directly modifying hard-linked files will alter the originals in
storage!***

## Torrent pre-fill
Already have most of a torrent's files in your library? This feature links them
into the download folder first, so your torrent client only fetches what's
missing. It runs as three steps:

1. **Match files** — quick name and size comparison between the torrent and the
   selected library entry.
2. **Verify pieces** — byte-exact check against the pieces in the `.torrent`
   file. Only files whose pieces all match 100% are eligible, which is why your
   client will not overwrite the linked files.
3. **Link verified files** — hard-links every verified file into the download
   folder, recreating the torrent's folder structure. Point your torrent client
   here and let it download the rest.

Inputs: the **library entry to pre-fill from** (pick the one sharing the most
files with the torrent), the **torrent file**, the **data folder inside the
torrent** (usually `contents\data`), and the **download folder**.

![Torrent pre-fill usage](https://stn.s-ul.eu/mIFRwafZ.png)

# How does it save space?

When adding more entries, only unique files are added to storage. Say you have
IIDX28 in your library and then add your IIDX27 data as well: only files unique
to IIDX27 are stored, so storage grows by about 4GB instead of 60GB.

![screenshot](https://stn.s-ul.eu/O84VELWa.png)
