using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels;

namespace LincleLINK.App.Services;

/// <summary>
/// Owns the process-global exception hooks and the error-report window
/// (issue #16 D1/D2). Created and installed at the top of
/// <c>App.OnFrameworkInitializationCompleted</c>, before the composition root
/// runs, so even bootstrap failures route through it; it deliberately takes no DI
/// dependencies, only the owner/window provider and the quit action. Every
/// presentation path is wrapped and must never throw, with <c>Console.Error</c>
/// as the terminal fallback.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionReporter
{
    // Loaded lazily and guarded: this handler is installed before anything else
    // runs, so a failure here must never throw at type-init and prevent the hooks
    // from ever being registered. A null icon is acceptable (Window.Icon is nullable).
    private static readonly Lazy<WindowIcon?> AppIcon = new(TryLoadAppIcon);

    private static WindowIcon? TryLoadAppIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://LincleLINK/Assets/LL_logo.ico")));
        }
        catch
        {
            return null;
        }
    }

    private readonly Func<Window?> _ownerProvider;
    private readonly Action _quit;

    private ExceptionReportViewModel? _reportVm;
    private Window? _window;
    private readonly List<TaskCompletionSource> _pendingCloseSignals = [];
    private bool _installed;
    private bool _isFatal;

    public GlobalExceptionHandler(Func<Window?> ownerProvider, Action quit)
    {
        _ownerProvider = ownerProvider;
        _quit = quit;
    }

    /// <summary>Registers the process-global hooks. Safe to call more than once.</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
    }

    public void ReportUnexpected(Exception exception) => Present(exception);

    /// <summary>
    /// Presents the report window in fatal mode and completes when the window is
    /// closed (callers then shut the process down). If no window appears within a
    /// short grace period it falls back to <c>Console.Error</c> with the full
    /// formatted report.
    /// </summary>
    public Task PresentFatalAsync(Exception exception)
    {
        _isFatal = true;
        var closed = new TaskCompletionSource();
        var shown = new TaskCompletionSource();

        if (Dispatcher.UIThread.CheckAccess())
        {
            Present(exception, closed, shown);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Present(exception, closed, shown);
                }
                catch (Exception ex)
                {
                    closed.TrySetException(ex);
                    shown.TrySetException(ex);
                }
            });
        }

        return AwaitFatalPresentationAsync(exception, shown, closed);
    }

    /// <summary>
    /// Best-effort fatal presentation from a non-UI thread (AppDomain hook, where
    /// the process is already doomed). Waits a short time for the window to appear;
    /// once shown it blocks until the window closes so the report stays readable
    /// instead of the CLR terminating the app under it. The <c>Console.Error</c>
    /// dump is the record when no window can be shown.
    /// </summary>
    public void PresentFatal(Exception exception)
    {
        _isFatal = true;
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Present(exception);
                return;
            }

            var closed = new TaskCompletionSource();
            var shown = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Present(exception, closed, shown);
                }
                catch (Exception ex)
                {
                    closed.TrySetException(ex);
                    shown.TrySetException(ex);
                }
            });

            if (!shown.Task.Wait(TimeSpan.FromSeconds(2)))
            {
                // The dispatcher never showed a window (or the pump is gone):
                // dump the full report as the record.
                Console.Error.WriteLine(ExceptionReport.Format(exception, DateTimeOffset.UtcNow, 1));
                return;
            }

            // Shown: keep the crashing thread blocked until the window closes.
            closed.Task.Wait();
        }
        catch
        {
            Console.Error.WriteLine(ExceptionReport.Format(exception, DateTimeOffset.UtcNow, 1));
        }
    }

    private static async Task AwaitFatalPresentationAsync(
        Exception exception, TaskCompletionSource shown, TaskCompletionSource closed)
    {
        try
        {
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // No window came up (dispatcher gone or never pumped): the console
            // report is the record.
            Console.Error.WriteLine(ExceptionReport.Format(exception, DateTimeOffset.UtcNow, 1));
            return;
        }

        // Shown: block until the window closes so the report stays readable.
        await closed.Task;
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Recoverable: marking the exception handled keeps the dispatcher loop running.
        e.Handled = true;
        Present(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        if (e.Exception is not { } aggregate)
        {
            return;
        }

        // Unwrap the single common case (one fire-and-forget task faulting).
        var exception = aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerExceptions[0]
            : aggregate;

        // GC-timing-dependent, so this is the safety net, not the primary path;
        // marshal to the UI thread so the window is only touched there.
        Dispatcher.UIThread.Post(() => Present(exception));
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            PresentFatal(exception);
        }
    }

    private void Present(Exception exception, TaskCompletionSource? closeSignal = null, TaskCompletionSource? shownSignal = null)
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Present(exception, closeSignal, shownSignal));
                return;
            }

            // One window at a time: while a report is open, further exceptions
            // accumulate into it instead of spawning new windows (D4). A fatal
            // caller arriving while a recoverable report is open upgrades that
            // window so the messaging and quit-on-close match, then stays blocked
            // on its close signal until the window actually closes.
            if (_reportVm is not null)
            {
                if (_isFatal && !_reportVm.IsFatal)
                {
                    _reportVm.MakeFatal();
                    if (_window is not null)
                    {
                        _window.Title = _reportVm.Title;
                    }
                }

                _reportVm.AddException(exception);

                if (closeSignal is not null)
                {
                    _pendingCloseSignals.Add(closeSignal);
                }

                shownSignal?.TrySetResult();
                return;
            }

            ShowWindow(exception, closeSignal, shownSignal);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not show the error report: " + ex);
            closeSignal?.TrySetException(ex);
            shownSignal?.TrySetException(ex);
        }
    }

    private void ShowWindow(Exception exception, TaskCompletionSource? closeSignal, TaskCompletionSource? shownSignal)
    {
        var vm = new ExceptionReportViewModel(exception, _isFatal, _quit);
        var window = new Window
        {
            Title = vm.Title,
            Content = vm,
            Width = vm.DialogSize.Width,
            Height = vm.DialogSize.Height,
            MinWidth = vm.DialogMinSize.Width,
            MinHeight = vm.DialogMinSize.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Icon = AppIcon.Value,
        };
        ThemeManager.ApplyTitleBar(window);
        vm.AttachWindow(window);

        // Continue (and Esc, its IsCancel binding) ask the view model to close;
        // close the window on CloseRequested, mirroring the DialogService pattern.
        vm.CloseRequested += (_, _) => window.Close();

        window.Opened += (_, _) => shownSignal?.TrySetResult();

        if (closeSignal is not null)
        {
            _pendingCloseSignals.Add(closeSignal);
        }

        window.Closed += (_, _) =>
        {
            // Closing the window by any means (Quit, X, Esc) quits only when this
            // window is fatal - fatality is per-window, so a report upgraded by
            // MakeFatal quits even though it opened in recoverable mode.
            if (vm.IsFatal)
            {
                _quit();
            }

            foreach (var signal in _pendingCloseSignals)
            {
                signal.TrySetResult();
            }

            _pendingCloseSignals.Clear();

            if (ReferenceEquals(_reportVm, vm))
            {
                _reportVm = null;
            }

            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };

        _reportVm = vm;
        _window = window;

        var owner = _ownerProvider();
        if (owner is not null)
        {
            if (!owner.IsVisible)
            {
                owner.Show();
            }

            // Fire-and-forget, but internally catching: a failure to host must
            // never surface as an unobserved task exception from inside the handler.
            _ = ShowDialogGuardedAsync(owner, window);
        }
        else
        {
            window.Show();
        }
    }

    private static async Task ShowDialogGuardedAsync(Window owner, Window window)
    {
        try
        {
            await window.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not show the error report window: " + ex);
        }
    }
}
