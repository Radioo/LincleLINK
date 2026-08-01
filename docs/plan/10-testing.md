# 10 — Testing Strategy

**Parent:** [00-high-level.md](00-high-level.md) §8; consolidates the test plans in `02`–`09`
**Milestone:** throughout (M1–M6), hardened at M6
**Status:** Approved

## 1. Scope

Umbrella conventions + framework setup for all v3 tests. Detailed per-area cases already live in `02`–`09` (referenced below); this doc fixes the tooling, fixtures, doubles policy, and CI wiring.

## 2. Frameworks & projects

- **xUnit** + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`
- **FluentAssertions** for assertions
- **NSubstitute** for mocks
- **coverlet.collector** for coverage

```
tests/LincleLINK.Core.Tests/   # domain, infra, application (filesystem-backed + mocked)
tests/LincleLINK.App.Tests/    # VM logic only — no rendering (AGENTS: no render-capture)
```

- Verification commands: `dotnet build LincleLINK.sln` then `dotnet test LincleLINK.sln`.
- `TreatWarningsAsErrors` on for both test projects (inherited from Core-side policy; `01` D1).

## 3. Fixtures & helpers (`TestHelpers/`)

| Helper | Purpose | Home |
|---|---|---|
| `TempDir` | `IDisposable` temp root; per-test isolation, cleanup in `Dispose` | Core.Tests |
| `TestData` | small-file builders; sample v2 instance JSON; generated `.torrent`; sample `DBInfo.xml` | Core.Tests |
| `PlatformGuard` | `[Fact]` skip helper for Windows/Linux-only assertions (see §6) | Core.Tests |
| `V2GoldenData` | byte-level v2 fixture strings + `TorrentPiecer` expected outputs | Core.Tests |

**No shared mutable state between tests.** Every test gets its own `TempDir`; `IAppPaths` is always pointed at a temp root via `IAppPathsFactory` (never the real CWD).

## 4. Test layers

**Layer 1 — pure unit (no IO, fast):** `02` — `SizeFormatter`, `InstanceNameValidator`, `Instance` JSON round-trip vs v2 fixture; `04` — `PathNormalizer`, errno→message mapper; `07` — `TorrentPieceVerifier` (given in-memory bytes) incl. V2-golden equivalence.

**Layer 2 — filesystem-backed (real IO, small scale):** `03` — `JsonInstanceRepository`, `FileStore`, `JsonSettingsStore`, `AppPaths`, `FirstLaunchService`, `LegacyImporter`; `04` — `FileSystem`, `Md5FileHasher`, `DriveInfoProvider`; `06` — link/copy/unused integration on real `db/`.

**Layer 3 — mocked ports (NSubstitute):** `05` — `InstanceService`; `06` — `LinkingService`, `UnusedFilesService`, delete flow; `07` — `TorrentService`; `08` — `StatusService`. Mock set: `IFileSystem`, `IFileHasher`, `IFileStore`, `IInstanceRepository`, `IDriveInfoProvider`, `IHardLinker`, `IDialogService`, `ITorrentSource`, `ISettingsStore`.

**Layer 4 — VM logic (`App.Tests`):** `08`/`09` — `MainViewModel` gating matrix, input-edit gate resets, theme toggle persistence, add-instance dialog flow; `AddInstanceViewModel` mode mapping + error handling. Uses mocked services; CommunityToolkit.Mvvm runs headless — **no Avalonia runtime needed, no rendering**.

## 5. Golden / compatibility tests (highest priority)

These lock the "read v2 data unchanged, write identical" contract:

1. **Instance JSON round-trip:** serialize a fixture built from real v2 output → byte-equal (ignoring line endings); deserialize v2 fixture → fields match; null/missing collections → empty lists.
2. **Hashed-name compatibility:** `Md5FileHasher` output equals v2 `GetMD5Checksum` for the same fixture file.
3. **V2 piece verification equivalence:** v3 `TorrentPieceVerifier` computed piece hashes == v2 `TorrentPiecer` output for a fixture torrent.
4. **Legacy import:** sample `DBInfo.xml` → expected v3 `Instance` fields incl. leading-`\` strip and dir derivation.

## 6. Platform-conditional tests

- `Win32HardLinker` / `UnixHardLinker`: link creation, source-delete keeps target (runs on the CI OS present; skipped otherwise via `PlatformGuard`).
- `DriveInfoProvider` / `UnixStatFsDriveInfoProvider`: run on their respective OS in CI matrix.
- `EXDEV` / errno mapping: unit-tested via the error-mapper directly (no cross-volume fixture).

CI runs the matrix on `windows-latest` **and** `ubuntu-latest` (`11-ci`), so each platform path is exercised on its host.

## 7. Coverage goals & reporting

- **Core:** ≥ 80% line coverage for `Domain` + `Application`; infra covered via the golden + TempDir tests (no strict threshold on P/Invoke paths).
- **App:** VM logic covered; XAML correctness via compile-time bindings (no unit target).
- Run `dotnet test --collect:"XPlat Code Coverage"` in CI; publish the `cobertura` report as a PR artifact (details in `11-ci`).

## 8. Doubles policy & hygiene

- Mock **ports only**; never mock concrete services unless unavoidable. Prefer real-IO `TempDir` tests over mocking `IFileSystem` when the behavior under test *is* IO.
- Cancellation tests assert `OperationCanceledException` propagates and no partial side effects (instance not saved; no links created).
- Keep fixture sizes small (KB range) so the suite runs in seconds; large-file streaming hashing is covered by one mid-size file test, not the whole suite.
- No network access in tests (torrent fixtures are generated locally, not downloaded).

## 9. Risks & mitigations

- **Golden data drift** if a v2 fixture is regenerated later → fixtures are committed, never regenerated silently; changing them requires a reviewed decision.
- **Semi/DataGrid XAML** not covered by tests → relies on build-time compile + M5 manual QA (AGENTS-aligned).
- **Platform-only bugs** (paths, hardlinks, statvfs) → CI matrix is the safety net.

## 10. Decisions (locked)

- **D1** Two test projects: Core (unit+FS+mocked) and App (VM logic only, no rendering).
- **D2** Golden/compat fixtures committed for JSON, hashing, piece verification, legacy import.
- **D3** Coverage gate ≥ 80% on Core `Domain`+`Application`; CI collects coverage.
- **D4** Ports-only mocking; real-IO `TempDir` preferred where behavior is IO.
