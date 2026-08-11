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
    private static readonly WindowIcon AppIcon =
        new(AssetLoader.Open(new Uri("avares://LincleLINK/Assets/LL_logo.ico")));

    private readonly Func<Window?> _ownerProvider;
    private readonly Action _quit;

    private ExceptionReportViewModel? _reportVm;
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
    /// closed (callers then shut the process down). If no UI can be shown it falls
    /// back to <c>Console.Error</c> with the full formatted report.
    /// </summary>
    public Task PresentFatalAsync(Exception exception)
    {
        _isFatal = true;
        var closed = new TaskCompletionSource();

        if (Dispatcher.UIThread.CheckAccess())
        {
            Present(exception, closed);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Present(exception, closed);
                }
                catch (Exception ex)
                {
                    closed.TrySetException(ex);
                }
            });
        }

        return closed.Task;
    }

    /// <summary>
    /// Best-effort fatal presentation from a non-UI thread (AppDomain hook, where
    /// the process is already doomed). Blocks briefly to give the window a chance
    /// to appear; the <c>Console.Error</c> dump is the record when the dispatcher
    /// is gone.
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
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Present(exception, closed);
                }
                catch (Exception ex)
                {
                    closed.TrySetException(ex);
                }
            });

            if (!closed.Task.Wait(TimeSpan.FromSeconds(2)))
            {
                // The dispatcher never ran the callback (or the window is still up
                // while the process is dying): dump the full report as the record.
                Console.Error.WriteLine(ExceptionReport.Format(exception, DateTimeOffset.UtcNow, 1));
            }
        }
        catch
        {
            Console.Error.WriteLine(ExceptionReport.Format(exception, DateTimeOffset.UtcNow, 1));
        }
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

    private void Present(Exception exception, TaskCompletionSource? closeSignal = null)
    {
        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => Present(exception, closeSignal));
                return;
            }

            // One window at a time: while a report is open, further exceptions
            // accumulate into it instead of spawning new windows (D4). A caller
            // waiting on a close signal (fatal mode) is released immediately when
            // the existing window already covers the report.
            if (_reportVm is not null)
            {
                _reportVm.AddException(exception);
                closeSignal?.TrySetResult();
                return;
            }

            ShowWindow(exception, closeSignal);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not show the error report: " + ex);
        }
    }

    private void ShowWindow(Exception exception, TaskCompletionSource? closeSignal)
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
            Icon = AppIcon,
        };
        ThemeManager.ApplyTitleBar(window);
        vm.AttachWindow(window);

        // Continue (and Esc, its IsCancel binding) ask the view model to close;
        // close the window on CloseRequested, mirroring the DialogService pattern.
        vm.CloseRequested += (_, _) => window.Close();

        window.Closed += (_, _) =>
        {
            // In fatal mode closing the window by any means (Quit, X, Esc) quits;
            // recoverable mode only reports, so a close is just a dismiss.
            if (_isFatal)
            {
                _quit();
            }

            closeSignal?.TrySetResult();
            _reportVm = null;
        };

        _reportVm = vm;

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
