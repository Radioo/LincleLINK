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

    public WindowsDriver<WindowsElement> Driver { get; }

    /// <summary>Per-session scratch root; also holds the settings file and data dir.
    /// Everything created here shares one volume, so hard-link flows work.</summary>
    public string TempRoot { get; }

    /// <summary>Settings file the app was pointed at (may not exist yet on first run).</summary>
    public string SettingsFile { get; }

    /// <summary>Data directory; also the app's working directory, so the first-run
    /// dialog proposes it as the default candidate.</summary>
    public string DataDirectory { get; }

    private AppSession(WindowsDriver<WindowsElement> driver, string tempRoot, string settingsFile, string dataDirectory)
    {
        Driver = driver;
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

        var options = new AppiumOptions();
        options.AddAdditionalCapability("app", ResolveAppPath());
        options.AddAdditionalCapability("appArguments", $"--settings-file=\"{settingsFile}\"");
        options.AddAdditionalCapability("appWorkingDir", dataDirectory);
        options.AddAdditionalCapability("platformName", "Windows");
        options.AddAdditionalCapability("deviceName", "WindowsPC");
        // Give WinAppDriver a few extra seconds to find the top-level window; the
        // app builds its DI container and runs EF migrations before showing it.
        options.AddAdditionalCapability("ms:waitForAppLaunch", "5");

        var serverUrl = Environment.GetEnvironmentVariable("WINAPPDRIVER_URL") ?? "http://127.0.0.1:4723";
        var driver = new WindowsDriver<WindowsElement>(new Uri(serverUrl), options, TimeSpan.FromSeconds(90));
        return new AppSession(driver, tempRoot, settingsFile, dataDirectory);
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

    /// <summary>
    /// Drives the native Windows folder picker: focuses the address bar (Alt+D),
    /// navigates to <paramref name="folderPath"/>, and confirms with the
    /// "Select Folder" button (which picks the currently open folder).
    /// </summary>
    public void CompleteFolderPicker(string titleContains, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        SwitchToWindowWithTitle(titleContains);
        SendGlobalKeys(Keys.Alt + "d" + Keys.Alt);
        SendGlobalKeys(folderPath + Keys.Enter);
        Thread.Sleep(1000);
        WaitForText("Select Folder", TimeSpan.FromSeconds(10)).Click();
    }

    /// <summary>
    /// Drives the native Windows file-open picker: focuses the file-name field
    /// (Alt+N), types the full path and confirms with Enter.
    /// </summary>
    public void CompleteFilePicker(string titleContains, string filePath)
    {
        SwitchToWindowWithTitle(titleContains);
        SendGlobalKeys(Keys.Alt + "n" + Keys.Alt);
        SendGlobalKeys(filePath + Keys.Enter);
    }

    /// <summary>Dismisses a native picker (or any focused dialog) with Escape.</summary>
    public void DismissDialogWithEscape(string titleContains)
    {
        SwitchToWindowWithTitle(titleContains);
        SendGlobalKeys(Keys.Escape);
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
            Driver.Quit();
        }
        catch
        {
            // The app may already have exited; nothing to clean up.
        }

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
