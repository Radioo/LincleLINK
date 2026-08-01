# 12 — Verification & Acceptance

**Parent:** [00-high-level.md](00-high-level.md) §9, §12 (M6); consolidates parity/QA requirements
**Milestone:** M6
**Status:** Approved

## 1. Scope

Define how v3 is proven done: the **feature-parity matrix**, **V2-data migration validation**, a **manual QA script** (per AGENTS — user tests, no screenshot harnesses), a **platform checklist**, the consolidated **behavior-change register**, and the **M6 acceptance gate**.

## 2. Feature-parity matrix (v2 → v3)

| # | Feature | Automated coverage | Manual check (QA) |
|---|---|---|---|
| F1 | Add instance (name/path, copy vs move, dedup, dirs, save) | `InstanceServiceTests` (`05`) + JSON golden (`10` §5) | Add a small folder; verify `db/` + `instance/<Name>.json` |
| F2 | Low-disk warning | `InstanceServiceTests` (`05`) | (optional) set `DataDirectory` to a nearly-full volume |
| F3 | Link instance (hard links, dup handling) | `LinkingServiceTests` + integration (`06`) | Link to a temp target; confirm files + same inode |
| F4 | Copy hashed files | `LinkingServiceTests` (`06`) | Copy to a temp dir; no overwrites |
| F5 | Delete instance (manifest only) | `InstanceServiceTests` (`06`) | Delete; `db/` files remain |
| F6 | Check unused files | `UnusedFilesServiceTests` (`06`) | Delete an instance → scan finds orphans → delete |
| F7 | Torrent: check files | `TorrentServiceTests` (`07`) | Check a real `.torrent`; path hint works |
| F8 | Torrent: check pieces | `TorrentPieceVerifierTests` + golden (`07`,`10`) | Check pieces; matched count reported |
| F9 | Torrent: link to torrent | `TorrentServiceTests` (`07`) | Link; only clean-piece files created |
| F10 | Import DBInfo.xml (v1) | `LegacyImporterTests` (`03`) | Import a real v1 `DBInfo.xml` |
| F11 | Status panel (db size/savings/free) | `StatusServiceTests` (`08`) | Values match `du`/Explorer |
| F12 | Dark mode + persistence | `MainViewModelTests` (`08`,`09`) | Toggle; restart; persists |
| F13 | Log panel + progress | `MainViewModelTests` (`08`) | Watch a long op |

## 3. V2-data migration validation

Prereq: a copy of a real v2 install (`db/` + `instance/*.json` + CWD `settings.json`).

1. First launch with the v2 data dir as CWD → **auto-adopt** (`AdoptCurrentDirectory`), no prompt.
2. Legacy `settings.json` dark flag is imported (if dark, v3 starts dark).
3. Fresh launch with no data → first-run prompt → pick a dir with v2 data → adopted.
4. Instance list shows all v2 instances; open one → file records identical.
5. Hash a new small instance → `db/` names match v2 format (uppercase MD5 + ext); no re-copy of existing hashes (dedup works against v2 data).
6. Link one v2 instance → structure matches original relative paths (incl. `\`-stored paths on Linux).
7. v2 `settings.json` is left untouched after migration.

## 4. Manual QA script (user runs; no render capture)

**Windows (fresh app-data):**
1. Run → first-run prompt → choose/keep a scratch data dir.
2. Add instance (copy mode) on a small tree → verify `db/` + instance JSON; add again on an overlapping tree → only new files added.
3. Link instance → verify hard link (delete the linked file → `db/` intact).
4. Copy hashed files → flat copies, no overwrite.
5. Delete instance → files remain; Check unused → delete orphans.
6. Torrent: check files → check pieces → link (real `.torrent`).
7. Toggle dark mode → UI + title bar dark → restart → persists.
8. Import a v1 `DBInfo.xml`.

**Linux (same core steps):**
1–8 as above (dark-mode title bar: no-op expected).
9. Open an instance created on Windows (backslash paths) → link → target structure correct.
10. Free-space line matches the volume of `DataDirectory`; add-instance low-disk path sane.

## 5. Platform checklist

- Hard links: Windows (`CreateHardLinkW`) and Linux (`link()`) both produce real hard links; cross-device error surfaces per-file, op continues.
- Paths: `\`/`/` normalization; no path traversal from stored/torrent data (`IsSafeRelativePath`).
- Case: instance-name uniqueness case-insensitive on both OS.
- Torrents: v1 verified; v2/hybrid rejected with clear message.
- Settings live in per-OS config (`%APPDATA%` / `~/.config`); user data in `DataDirectory`.

## 6. Behavior-change register (v2 → v3, consolidated)

| Change | Plan | Reason |
|---|---|---|
| `ReadableSize` boundary fix (exact `1024^n`) | `02` D2 | v2 bug |
| `TotalFileSizeString` recomputed on save | `02` D3 | consistency |
| `settings.json` moved to per-OS config; `DataDirectory` setting | `03` | split settings from data |
| V2 data adopted at first launch (non-destructive) | `03` | migration |
| Atomic instance save (temp + move) | `03` D3 | crash safety |
| `FileStore` hash-name traversal guard | `03` D4 | security |
| Low-disk free space measured on data-path volume | `05` D2 | v2 measured wrong drive |
| Zero-file instances rejected | `05` D4 | useless/confusing state |
| Move mode: copy into `db/` + hard-link the original back (was `File.Move`, which deleted the source and crashed on re-stat) | `05` | critical bug fix + requested behavior |
| Per-file link failures log + continue (not abort-all) | `06`/`07` | resilience |
| Stored-data-derived target paths validated | `06` D3 | security |
| Streaming piece verification (bounded memory) | `07` D2 | v2 OOM risk |
| Canonicalized torrent/instance path matching | `07` D4 | v2 Linux mismatch |
| V1-only torrents; v2/hybrid rejected | `07` D1 | scoped v2 support |
| `IsFree`/`UIContext` replaced by `IsBusy` + `IProgress` | `08` | concurrency correctness |
| UI redesign (Semi), layout preserved | `09` | modernization |

## 7. M6 acceptance gate

All must hold before the rewrite is considered complete:

- [ ] CI green on Windows + Linux (build + tests).
- [ ] `10` §5 golden/compat tests pass (v2 data contract locked).
- [ ] All F1–F13 parity checks pass on both OS (manual QA §4).
- [ ] Migration validation §3 passes.
- [ ] Platform checklist §5 passes.
- [ ] Coverage report ≥ 80% on Core `Domain`+`Application` (target).
- [ ] Behavior-change register §6 reviewed and acknowledged by the owner.
- [ ] README + AGENTS.md rewritten for v3; `docs/plan/` referenced from README.
- [ ] Release workflow produces runnable Windows + Linux artifacts (`11` §5).

## 8. Decisions (locked)

- **D1** Manual QA is run by the user on both OS per §4; automated tests cover everything testable headless.
- **D2** The behavior-change register is the single source of truth for deliberate v2 deviations; owner sign-off required at M6.
