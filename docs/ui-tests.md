# Appium UI tests

`tests/LincleLINK.UITests/` drives the real `LincleLINK.exe` through
[WinAppDriver](https://github.com/microsoft/WinAppDriver) (Appium's Windows
driver) using the Appium .NET client. The tests are black-box: they launch the
app, click through the first-run flow, navigate the shell, and assert against
both the UI (via UIA `AutomationId`s) and on-disk effects (settings file,
storage layout).

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
- The client is `Appium.WebDriver` 4.4.5 on purpose: the 4.x line is the last
  one that speaks WinAppDriver's legacy JSON Wire Protocol directly; 5.x is
  W3C-only and would require a full Node-based Appium 2 server in between.
