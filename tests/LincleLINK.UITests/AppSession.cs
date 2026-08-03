using System.Diagnostics;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Interactions;

namespace LincleLINK.UITests;

/// <summary>
/// One launched LincleLINK instance driven over WinAppDriver. Every session gets
/// its own temp root (settings file + data directory) via the app's
/// <c>--settings-file</c> switch, so tests never touch the real user profile and
/// can run from any starting state.
/// </summary>
public sealed class AppSession : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly string _serverUrl;
    private readonly string _appPath;
    private WindowsDriver<WindowsElement>? _desktop;

    public WindowsDriver<WindowsElement> Driver { get; }

    /// <summary>
    /// A second WinAppDriver session rooted at the desktop. Native common dialogs
    /// (folder/file pickers) are not reliably enumerable through the app session's
    /// window handles, but they are ordinary top-level windows from the desktop's
    /// point of view.
    /// </summary>
    private WindowsDriver<WindowsElement> Desktop
    {
        get
        {
            if (_desktop is null)
            {
                var options = new AppiumOptions();
                options.AddAdditionalCapability("app", "Root");
                options.AddAdditionalCapability("platformName", "Windows");
                options.AddAdditionalCapability("deviceName", "WindowsPC");
                _desktop = new WindowsDriver<WindowsElement>(new Uri(_serverUrl), options, TimeSpan.FromSeconds(60));
            }

            return _desktop;
        }
    }

    /// <summary>Per-session scratch root; also holds the settings file and data dir.
    /// Everything created here shares one volume, so hard-link flows work.</summary>
    public string TempRoot { get; }

    /// <summary>Settings file the app was pointed at (may not exist yet on first run).</summary>
    public string SettingsFile { get; }

    /// <summary>Data directory; also the app's working directory, so the first-run
    /// dialog proposes it as the default candidate.</summary>
    public string DataDirectory { get; }

    private AppSession(
        WindowsDriver<WindowsElement> driver,
        string serverUrl,
        string appPath,
        string tempRoot,
        string settingsFile,
        string dataDirectory)
    {
        Driver = driver;
        _serverUrl = serverUrl;
        _appPath = appPath;
        TempRoot = tempRoot;
        SettingsFile = settingsFile;
        DataDirectory = dataDirectory;
    }

    /// <param name="seedSettings">When true, writes a settings file up front so the
    /// app boots straight into the main shell; when false, the app runs its
    /// first-launch flow.</param>
    /// <param name="theme">Theme to seed ("Light"/"Dark"/"System").</param>
    /// <param name="threads">Hash thread count to seed.</param>
    public static AppSession Launch(bool seedSettings, string theme = "Light", int threads = 2)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "LincleLINK.UITests", Guid.NewGuid().ToString("N"));
        var dataDirectory = Path.Combine(tempRoot, "data");
        var settingsFile = Path.Combine(tempRoot, "config", "settings.json");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);

        if (seedSettings)
        {
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(new
            {
                Theme = theme,
                DataDirectory = dataDirectory,
                HashThreadCount = threads,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        var appPath = ResolveAppPath();

        // A previous failed test can leave an app instance behind (e.g. blocked on
        // a modal picker); it would hold desktop focus and confuse later sessions.
        KillStrayApps(appPath);

        var options = new AppiumOptions();
        options.AddAdditionalCapability("app", appPath);
        options.AddAdditionalCapability("appArguments", $"--settings-file=\"{settingsFile}\"");
        options.AddAdditionalCapability("appWorkingDir", dataDirectory);
        options.AddAdditionalCapability("platformName", "Windows");
        options.AddAdditionalCapability("deviceName", "WindowsPC");
        // Give WinAppDriver a few extra seconds to find the top-level window; the
        // app builds its DI container and runs EF migrations before showing it.
        options.AddAdditionalCapability("ms:waitForAppLaunch", "5");

        var serverUrl = Environment.GetEnvironmentVariable("WINAPPDRIVER_URL") ?? "http://127.0.0.1:4723";
        var driver = new WindowsDriver<WindowsElement>(new Uri(serverUrl), options, TimeSpan.FromSeconds(90));

        // CI screens can be as small as 1024x768; the app's default 1120-wide
        // window would push right-side controls (inspector, activity bar buttons)
        // offscreen where clicks silently miss. Pin it to a size that always fits.
        try
        {
            driver.Manage().Window.Position = new System.Drawing.Point(0, 0);
            driver.Manage().Window.Size = new System.Drawing.Size(1000, 700);
        }
        catch (WebDriverException)
        {
            // Cosmetic hardening only; never fail the launch over it.
        }

        return new AppSession(driver, serverUrl, appPath, tempRoot, settingsFile, dataDirectory);
    }

    /// <summary>Kills leftover app instances started from the test output copy of
    /// the exe (never a user's real install, which lives elsewhere).</summary>
    private static void KillStrayApps(string appPath)
    {
        foreach (var process in Process.GetProcessesByName("LincleLINK"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // Access denied or already exited; leave it alone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string ResolveAppPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LINCLELINK_APP_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        // The project reference copies the app (exe + runtime assets) next to the
        // test assembly, so the default is simply our own output directory.
        var local = Path.Combine(AppContext.BaseDirectory, "LincleLINK.exe");
        if (File.Exists(local))
        {
            return local;
        }

        throw new FileNotFoundException(
            $"LincleLINK.exe not found at '{local}'. Build the solution first, " +
            "or point LINCLELINK_APP_PATH at the exe to test.");
    }

    // ── element lookup ─────────────────────────────────────────────────────

    /// <summary>Finds an element by AutomationId, polling all of the app's
    /// top-level windows (modal dialogs get their own window handle).</summary>
    public WindowsElement WaitForId(string automationId, TimeSpan? timeout = null)
        => WaitForElement(d => d.FindElementsByAccessibilityId(automationId), $"AutomationId '{automationId}'", timeout);

    /// <summary>Finds an element by its UIA name (a TextBlock's name is its text).</summary>
    public WindowsElement WaitForText(string name, TimeSpan? timeout = null)
        => WaitForElement(d => d.FindElementsByName(name), $"name '{name}'", timeout);

    /// <summary>Waits until no element with the AutomationId exists in the current window.</summary>
    public void WaitForGoneById(string automationId, TimeSpan? timeout = null)
        => WaitUntil(() => CountSafe(() => Driver.FindElementsByAccessibilityId(automationId).Count) == 0,
            $"AutomationId '{automationId}' gone", timeout);

    /// <summary>Waits until no element with the given UIA name exists in the current window.</summary>
    public void WaitForGoneByName(string name, TimeSpan? timeout = null)
        => WaitUntil(() => CountSafe(() => Driver.FindElementsByName(name).Count) == 0,
            $"name '{name}' gone", timeout);

    private static int CountSafe(Func<int> count)
    {
        try
        {
            return count();
        }
        catch (WebDriverException)
        {
            // The window itself is gone; that counts as "element gone".
            return 0;
        }
    }

    /// <summary>Polls until <paramref name="condition"/> holds (for out-of-process
    /// effects such as the settings file being written).</summary>
    public void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"Condition not met within {timeout ?? DefaultTimeout}: {description}");
    }

    /// <summary>Waits for the activity bar's idle outcome line to start with the
    /// given text (e.g. "✓ Deployed"). Checks both the normal and warning slots.</summary>
    public void WaitForOutcome(string expectedStart, TimeSpan? timeout = null)
        => WaitUntil(
            () => OutcomeText("ActivityOutcome").StartsWith(expectedStart, StringComparison.Ordinal)
                  || OutcomeText("ActivityOutcomeWarning").StartsWith(expectedStart, StringComparison.Ordinal),
            $"activity outcome starting with '{expectedStart}'",
            timeout);

    private string OutcomeText(string id)
    {
        try
        {
            var found = Driver.FindElementsByAccessibilityId(id);
            return found.Count > 0 ? found.First().Text : string.Empty;
        }
        catch (WebDriverException)
        {
            return string.Empty;
        }
    }

    private WindowsElement WaitForElement(
        Func<WindowsDriver<WindowsElement>, IReadOnlyCollection<WindowsElement>> query,
        string description,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var found = query(Driver);
                if (found.Count > 0)
                {
                    return found.First();
                }
            }
            catch (WebDriverException e)
            {
                // The current window may just have closed (e.g. a dialog);
                // fall through to the handle scan.
                lastError = e;
            }

            try
            {
                foreach (var handle in Driver.WindowHandles)
                {
                    try
                    {
                        Driver.SwitchTo().Window(handle);
                        var found = query(Driver);
                        if (found.Count > 0)
                        {
                            return found.First();
                        }
                    }
                    catch (WebDriverException e)
                    {
                        lastError = e;
                    }
                }
            }
            catch (WebDriverException e)
            {
                lastError = e;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"UI element not found within {timeout ?? DefaultTimeout}: {description}", lastError);
    }

    // ── input helpers ──────────────────────────────────────────────────────

    /// <summary>Replaces a TextBox's content: focus, select-all, delete, type.</summary>
    public void SetText(WindowsElement element, string text)
    {
        element.Click();
        element.SendKeys(Keys.Control + "a" + Keys.Control);
        element.SendKeys(Keys.Delete);
        if (text.Length > 0)
        {
            element.SendKeys(text);
        }
    }

    /// <summary>Types into whatever currently has keyboard focus (modal pickers).</summary>
    public void SendGlobalKeys(string keys) => new Actions(Driver).SendKeys(keys).Perform();

    /// <summary>Opens a ComboBox and selects its first item via the keyboard
    /// (popup lists are not reliably reachable through the window-handle scan).</summary>
    public void SelectFirstComboItem(string automationId)
    {
        var combo = WaitForId(automationId);
        combo.Click();
        Thread.Sleep(400);
        SendGlobalKeys(Keys.Down);
        SendGlobalKeys(Keys.Enter);
    }

    /// <summary>Clicks a button in the app's own message dialogs ("Yes"/"No"/"OK").</summary>
    public void ClickMessageButton(string buttonName, TimeSpan? timeout = null)
        => WaitForText(buttonName, timeout).Click();

    // ── native common-dialog helpers (folder/file pickers) ─────────────────

    /// <summary>Switches the driver to the app window whose title contains the text.</summary>
    public void SwitchToWindowWithTitle(string titleContains, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                foreach (var handle in Driver.WindowHandles)
                {
                    try
                    {
                        Driver.SwitchTo().Window(handle);
                        if (Driver.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                    catch (WebDriverException)
                    {
                        // Window closed mid-scan; keep looking.
                    }
                }
            }
            catch (WebDriverException)
            {
                // Handle list unavailable for a moment; retry.
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"No window with title containing '{titleContains}' appeared.");
    }

    /// <summary>Finds a top-level window by exact title from the desktop session
    /// (the only reliable way to reach native common dialogs).</summary>
    private WindowsElement WaitForDesktopWindow(string title, TimeSpan? timeout = null)
    {
        WindowsElement? window = null;
        WaitUntil(() =>
        {
            try
            {
                var found = Desktop.FindElementsByName(title);
                window = found.FirstOrDefault(w =>
                             w.TagName.Contains("Window", StringComparison.OrdinalIgnoreCase))
                         ?? found.FirstOrDefault();
                return window is not null;
            }
            catch (WebDriverException)
            {
                return false;
            }
        }, $"desktop window titled '{title}'", timeout);

        return window!;
    }

    private void WaitForDesktopWindowGone(string title, TimeSpan? timeout = null)
        => WaitUntil(() =>
        {
            try
            {
                return Desktop.FindElementsByName(title).Count == 0;
            }
            catch (WebDriverException)
            {
                return true;
            }
        }, $"desktop window titled '{title}' gone", timeout);

    /// <summary>
    /// Drives the native Windows folder picker (found by its exact title):
    /// focuses the address bar (Alt+D), navigates to <paramref name="folderPath"/>,
    /// and confirms with "Select Folder" (which picks the currently open folder).
    /// </summary>
    public void CompleteFolderPicker(string title, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        var dialog = WaitForDesktopWindow(title);
        dialog.SendKeys(Keys.Alt + "d" + Keys.Alt);
        dialog.SendKeys(folderPath + Keys.Enter);
        Thread.Sleep(1000);
        dialog.FindElementByName("Select Folder").Click();
        WaitForDesktopWindowGone(title);
    }

    /// <summary>
    /// Drives the native Windows file-open picker (found by its exact title):
    /// focuses the file-name field (Alt+N), types the full path, confirms with Enter.
    /// </summary>
    public void CompleteFilePicker(string title, string filePath)
    {
        var dialog = WaitForDesktopWindow(title);
        dialog.SendKeys(Keys.Alt + "n" + Keys.Alt);
        dialog.SendKeys(filePath + Keys.Enter);
        WaitForDesktopWindowGone(title);
    }

    /// <summary>Dismisses a native picker (found by its exact title) with Escape.</summary>
    public void DismissDialogWithEscape(string title)
    {
        var dialog = WaitForDesktopWindow(title);
        dialog.SendKeys(Keys.Escape);
        WaitForDesktopWindowGone(title);
    }

    // ── diagnostics / teardown ─────────────────────────────────────────────

    /// <summary>Best-effort screenshot + UIA tree dump for failure triage; CI
    /// uploads the artifacts directory.</summary>
    public void TryDumpArtifacts(string testName)
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable("LINCLELINK_UITEST_ARTIFACTS")
                      ?? Path.Combine(AppContext.BaseDirectory, "ui-test-artifacts");
            Directory.CreateDirectory(dir);
            Driver.GetScreenshot().SaveAsFile(Path.Combine(dir, testName + ".png"), ScreenshotImageFormat.Png);
            File.WriteAllText(Path.Combine(dir, testName + ".xml"), Driver.PageSource);
        }
        catch
        {
            // Artifact capture must never mask the original test failure.
        }
    }

    public void Dispose()
    {
        try
        {
            _desktop?.Quit();
        }
        catch
        {
            // Desktop session teardown is best-effort.
        }

        try
        {
            Driver.Quit();
        }
        catch
        {
            // The app may already have exited; nothing to clean up.
        }

        // Quit cannot close an app blocked on a modal native dialog; make sure
        // nothing survives to steal focus from the next session.
        KillStrayApps(_appPath);

        // The app can still be flushing SQLite on exit; retry briefly.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(TempRoot, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
