# Appium UI tests

`tests/LincleLINK.UITests/` drives the real `LincleLINK.exe` through
[WinAppDriver](https://github.com/microsoft/WinAppDriver) (Appium's Windows
driver) using the Appium .NET client. The tests are black-box: they launch the
app, click through its screens, and assert against both the UI (via UIA
`AutomationId`s) and on-disk effects (settings file, storage layout, deployed
and linked files).

## Coverage map (screen by screen)

| Screen | Suite | Interactions covered |
| --- | --- | --- |
| First run | `FirstRunTests` | Accept proposed directory; empty-directory validation; typing a custom directory (with spaces); theme radios; persistence of all choices. |
| Shell | `MainShellTests` | Seeded boot skips first run; sidebar navigation across all three pages; activity-log drawer toggle + log lines; storage card figures. |
| Library | `LibraryTests` | Add folder in both modes (Keep originals / Reclaim space) incl. dedup and originals-intact checks; empty-folder error dialog; filter box narrowing/restoring; row selection + inspector (name, unique size, action buttons); remove with No/Yes confirmation; deploy to folder (native picker, files verified on disk); export hashed blobs (native picker); storage cleanup both when clean and with orphans. |
| Settings | `SettingsTests` | Theme radios persist; thread slider persists; change data directory (native picker + restart notice + pending note); legacy import picker cancel. |
| Torrent pre-fill | `TorrentTests` | Initial gating + hint texts; full wizard against a generated fixture torrent: entry combo, typed paths, match -> verify -> link, files verified in the download folder. |

Not covered (native pickers only reachable through the "Browse..." buttons are
exercised via the deploy/export/change-directory/import flows instead; the
in-flight Cancel buttons need an operation slow enough to catch, which tiny
fixtures do not provide).

## How isolation works

Every test session gets its own temp root and passes
`--settings-file=<temp>\config\settings.json` to the app (a switch handled in
`AppBootstrapper`), with the working directory set to `<temp>\data`. Tests never
read or write the real `%APPDATA%\LincleLINK` profile. Seeded tests write a
settings file up front; the first-run test starts from nothing.

## Running locally

The tests are skipped unless explicitly enabled, so `dotnet test` on the
solution stays green on machines without the infrastructure.

One-time setup (both need admin):

1. Enable Windows **Developer Mode** (Settings > System > For developers).
   WinAppDriver refuses to start without it.
2. Install [WinAppDriver v1.2.1](https://github.com/microsoft/WinAppDriver/releases/tag/v1.2.1).

Then:

```powershell
# 1. Start the driver (leaves a console window open, listens on 127.0.0.1:4723)
& "C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe"

# 2. In another terminal:
dotnet build LincleLINK.sln -c Release
$env:LINCLELINK_UI_TESTS = '1'
dotnet test tests/LincleLINK.UITests -c Release --no-build
```

Do not touch the mouse or keyboard while the tests run; they automate the
foreground desktop.

Environment knobs:

| Variable | Default | Purpose |
| --- | --- | --- |
| `LINCLELINK_UI_TESTS` | unset | Set to `1` to actually run the tests (otherwise skipped). |
| `WINAPPDRIVER_URL` | `http://127.0.0.1:4723` | Where the WinAppDriver server listens. |
| `LINCLELINK_APP_PATH` | exe copied next to the test dll | Override which build of the app to test. |
| `LINCLELINK_UITEST_ARTIFACTS` | `<test bin>/ui-test-artifacts` | Where failure screenshots / UIA dumps go. |

## CI

The `ui-test` job in `.github/workflows/ci.yml` runs on `windows-latest`: it
enables Developer Mode via the registry, installs and starts WinAppDriver, and
runs the suite with `LINCLELINK_UI_TESTS=1`. On failure it uploads screenshots,
UIA tree dumps, and the trx log as the `ui-test-artifacts` artifact.

## Writing tests

- Find elements by `AutomationProperties.AutomationId` (set in the axaml), via
  `AppSession.WaitForId`. Text lookup (`WaitForText`) works for TextBlocks,
  whose UIA name is their text.
- Both helpers poll all top-level windows of the app, so elements inside modal
  dialogs (which get their own window handle) are found transparently.
- Wrap test bodies in `Run(app, () => ...)` (from `UITestBase`) so failures
  capture a screenshot and UIA tree automatically.
- `UITestBase.AddEntry` drives the whole add-folder flow; `TestData` creates
  the standard 4-file source folder (3 unique blobs) and generates v1 .torrent
  fixtures with MonoTorrent.
- App-owned dialogs (confirm/info/error) are plain Avalonia windows: click
  their buttons by name via `ClickMessageButton("Yes"/"No"/"OK")`.
- Native pickers are reached through a second, desktop-rooted WinAppDriver
  session (the app session's window-handle list does not reliably include
  common dialogs) and located by their exact window title. They are driven
  keyboard-first: `CompleteFolderPicker` navigates via the address bar (Alt+D)
  and clicks "Select Folder"; `CompleteFilePicker` types into the file-name
  field (Alt+N); `DismissDialogWithEscape` cancels. These are the flakiest
  helpers - prefer typed path TextBoxes where the UI offers them.
- App-dialog assertions go through `SwitchToWindowWithTitle` (window titles),
  not the message TextBlock's text - text lookup inside message dialogs proved
  unreliable under UIA.
- `AppSession.Launch` pins the window to 1000x700 at the origin: CI screens can
  be 1024x768, and offscreen controls fail clicks silently. It also kills stray
  app instances (a test that dies with a modal picker open leaves a zombie).
- Assert operation results through the activity bar (`WaitForOutcome("✓ ...")`)
  plus on-disk effects, not just element presence.
- The client is `Appium.WebDriver` 4.4.5 on purpose: the 4.x line is the last
  one that speaks WinAppDriver's legacy JSON Wire Protocol directly; 5.x is
  W3C-only and would require a full Node-based Appium 2 server in between.
