using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

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

    private readonly string _tempRoot;

    public WindowsDriver<WindowsElement> Driver { get; }

    /// <summary>Settings file the app was pointed at (may not exist yet on first run).</summary>
    public string SettingsFile { get; }

    /// <summary>Data directory; also the app's working directory, so the first-run
    /// dialog proposes it as the default candidate.</summary>
    public string DataDirectory { get; }

    private AppSession(WindowsDriver<WindowsElement> driver, string tempRoot, string settingsFile, string dataDirectory)
    {
        Driver = driver;
        _tempRoot = tempRoot;
        SettingsFile = settingsFile;
        DataDirectory = dataDirectory;
    }

    /// <param name="seedSettings">When true, writes a settings file up front so the
    /// app boots straight into the main shell; when false, the app runs its
    /// first-launch flow.</param>
    /// <param name="theme">Theme to seed ("Light"/"Dark"/"System").</param>
    public static AppSession Launch(bool seedSettings, string theme = "Light")
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
                HashThreadCount = 2,
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

    /// <summary>Finds an element by AutomationId, polling all of the app's
    /// top-level windows (modal dialogs get their own window handle).</summary>
    public WindowsElement WaitForId(string automationId, TimeSpan? timeout = null)
        => WaitForElement(d => d.FindElementsByAccessibilityId(automationId), $"AutomationId '{automationId}'", timeout);

    /// <summary>Finds an element by its UIA name (a TextBlock's name is its text).</summary>
    public WindowsElement WaitForText(string name, TimeSpan? timeout = null)
        => WaitForElement(d => d.FindElementsByName(name), $"name '{name}'", timeout);

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
                // The current window may just have closed (e.g. the first-run
                // dialog); fall through to the handle scan.
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
                Directory.Delete(_tempRoot, recursive: true);
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
