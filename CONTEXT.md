# LincleLINK

LincleLINK keeps many versions of a game on one drive without storing the same file twice, and recreates any version in a folder on demand.

## Language

**Instance**:
A named list of files and directories that make up one game install, each file pointing at its content in Storage. The UI calls it a "library entry" or "entry".
_Avoid_: Manifest, version, game

**Storage**:
The pool of deduplicated file contents, each stored once and named by its hash. A file in Storage never changes after it is written.
_Avoid_: db, database, store, cache

**Deploy**:
Recreate an Instance in a folder using hard links into Storage. The app keeps no record of where an Instance was deployed.
_Avoid_: Link, install, extract

**Update**:
Add files to an existing Instance from a dropped or picked source, replacing the entries at paths that already exist. An Update never removes files and never changes a deployed folder.
_Avoid_: Patch, upgrade, merge

**Source**:
A folder or set of files the user drops or picks during an Update. Each Source has a Destination, and when two Sources hold the same path the later one wins.
_Avoid_: Pack, payload, input

**Destination**:
The folder inside the Instance where a Source's files land, the Instance root unless the user picks another. A Source that is a single folder counts as the Destination itself, so its contents merge into it.
_Avoid_: Target, mount point

**Removal**:
Taking a file or folder out of an Instance. The content stays in Storage until a storage cleanup finds it unreferenced, and deployed folders keep it.
_Avoid_: Delete

**Pending changes**:
The Sources, exclusions and Removals a user has staged against an Instance and not yet applied. They apply together or not at all.
_Avoid_: Draft, diff

**Duplicate**:
Create a new Instance under a new name with the same files and directories as an existing one. It copies no file contents.
_Avoid_: Clone, copy, fork
