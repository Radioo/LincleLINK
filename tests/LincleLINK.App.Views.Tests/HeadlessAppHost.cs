using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using LincleLINK.App;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Runs the Avalonia headless platform on a dedicated pumping UI thread, so every
/// test body that touches Avalonia can be marshaled onto that thread regardless of
/// which thread xUnit schedules the test on. Loads the app's XAML resource tree
/// (Semi theme, converters, logo assets) without running the real bootstrapper: a
/// subclass of <see cref="App"/> whose completion callback does nothing, so no DB
/// migrations, config-dir reads or window hosting happen. Lives in its own test
/// assembly so the global <c>Application.Current</c> it installs cannot change the
/// progress-marshaling behavior of the headless-free VM/service tests.
/// </summary>
public static class HeadlessAppHost
{
    private static readonly object Lock = new();
    private static Thread? _uiThread;
    private static Exception? _fatalError;

    public static void EnsureInitialized()
    {
        if (_uiThread is not null)
        {
            ThrowIfFatal();
            return;
        }

        lock (Lock)
        {
            if (_uiThread is not null)
            {
                ThrowIfFatal();
                return;
            }

            using var ready = new ManualResetEventSlim();
            var uiThread = new Thread(() =>
            {
                try
                {
                    AppBuilder.Configure<TestApp>()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                        .SetupWithoutStarting();

                    ready.Set();

                    // Pump the dispatcher until the process exits: RunJobs processes
                    // everything posted to the UI thread (test bodies, control events).
                    while (true)
                    {
                        try
                        {
                            Dispatcher.UIThread.RunJobs();
                        }
                        catch (Exception ex)
                        {
                            _fatalError = ex;
                            break;
                        }

                        Thread.Sleep(5);
                    }
                }
                catch (Exception ex)
                {
                    _fatalError = ex;
                    ready.Set();
                }
            })
            {
                IsBackground = true,
                Name = "LincleLINK headless UI thread",
            };
            if (OperatingSystem.IsWindows())
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }

            uiThread.Start();
            _uiThread = uiThread;
            ready.Wait();
            ThrowIfFatal();
        }
    }

    /// <summary>
    /// Executes <paramref name="action"/> on the headless UI thread and waits for it
    /// to complete. Avalonia controls may only be touched from that thread.
    /// </summary>
    public static void RunOnUiThread(Action action)
    {
        EnsureInitialized();
        ThrowIfFatal();

        var completed = new TaskCompletionSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.SetResult();
            }
        });

        completed.Task.Wait();
        if (failure is not null)
        {
            throw failure;
        }
    }

    /// <summary>Runs a value-returning lambda on the headless UI thread.</summary>
    public static T RunOnUiThread<T>(Func<T> func)
    {
        T? result = default;
        RunOnUiThread(() => { result = func(); });
        return result!;
    }

    private static void ThrowIfFatal()
    {
        if (_fatalError is not null)
        {
            throw new InvalidOperationException("The headless Avalonia UI thread failed.", _fatalError);
        }
    }

    private sealed class TestApp : App
    {
        public override void OnFrameworkInitializationCompleted()
        {
            // Initialize() (XAML load + brand theme) is invoked by the framework;
            // the real startup path is deliberately skipped here.
        }
    }
}
