using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// The error-report window (issue #16 D2): headline, summary card, full stack
/// trace, environment strip and the Copy report / Open GitHub issues / Continue /
/// Quit actions. One view model serves both the recoverable and the fatal modes
/// (bound <see cref="IsFatal"/>); the storm guard lives in the underlying
/// <see cref="ExceptionAccumulator"/>.
/// </summary>
public partial class ExceptionReportViewModel : ViewModelBase
{
    private readonly ExceptionAccumulator _accumulator;
    private readonly Action _onQuit;
    private Window? _hostWindow;

    public override string Title { get; }

    public override Size DialogSize => new(620, 480);

    public override Size DialogMinSize => new(520, 380);

    public bool IsFatal { get; }

    public string Headline => IsFatal
        ? "LincleLINK has to close"
        : "LincleLINK ran into an unexpected error";

    public string Helper => IsFatal
        ? "An error stopped the app from starting. Copy the report below and attach it to a GitHub issue so this can be fixed."
        : "You can keep using the app. If this keeps happening, please copy the report below and attach it to a GitHub issue.";

    public string SummaryType => _accumulator.Primary?.GetType().FullName ?? string.Empty;

    public string SummaryMessage => _accumulator.Primary?.Message ?? string.Empty;

    /// <summary>Occurrence badge ("×7"), empty until the same error repeats.</summary>
    public string OccurrencesText =>
        _accumulator.PrimaryOccurrences > 1 ? $"×{_accumulator.PrimaryOccurrences}" : string.Empty;

    public string DetailsText => _accumulator.BuildDetailsText();

    public string OverflowText => _accumulator.HasOverflow
        ? $"…and {_accumulator.OverflowCount} more distinct errors"
        : string.Empty;

    /// <summary>Same values that go into the copied Markdown report.</summary>
    public string EnvironmentStrip => ExceptionReport.EnvironmentLine;

    public bool NotFatal => !IsFatal;

    public bool QuitIsDefault => IsFatal;

    [ObservableProperty]
    private string _copyButtonText = "Copy report";

    public ExceptionReportViewModel(Exception exception, bool isFatal, Action onQuit)
    {
        _accumulator = new ExceptionAccumulator();
        _accumulator.Add(exception);
        _onQuit = onQuit;
        IsFatal = isFatal;
        Title = isFatal ? "LincleLINK could not start" : "Unexpected error - LincleLINK";
    }

    /// <summary>
    /// The hosting window, used as the clipboard's top level (always available,
    /// unlike the main window during startup failures).
    /// </summary>
    public void AttachWindow(Window window) => _hostWindow = window;

    /// <summary>Registers another occurrence while the report window is open (D4).</summary>
    public void AddException(Exception exception)
    {
        if (!_accumulator.Add(exception))
        {
            return;
        }

        OnPropertyChanged(nameof(SummaryType));
        OnPropertyChanged(nameof(SummaryMessage));
        OnPropertyChanged(nameof(OccurrencesText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(OverflowText));
    }

    /// <summary>
    /// The full Markdown report: the primary exception formatted by
    /// <see cref="ExceptionReport"/>, an "Also occurred" section per additional
    /// distinct exception, then the overflow line.
    /// </summary>
    public string BuildReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ExceptionReport.Format(
            _accumulator.Primary!, DateTimeOffset.UtcNow, _accumulator.PrimaryOccurrences));

        foreach (var extra in _accumulator.AdditionalExceptions())
        {
            sb.AppendLine();
            sb.AppendLine("### Also occurred");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(extra.ToString());
            sb.AppendLine("```");
        }

        if (_accumulator.HasOverflow)
        {
            sb.AppendLine();
            sb.AppendLine(OverflowText);
        }

        return sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task CopyReportAsync()
    {
        if (_hostWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(BuildReportText());
            CopyButtonText = "Copied ✓";
            await Task.Delay(2000);
            CopyButtonText = "Copy report";
        }
    }

    [RelayCommand]
    private void OpenGitHubIssues()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Radioo/LincleLINK/issues/new")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // The report is still on screen and copyable; a broken launcher is not fatal.
            Console.Error.WriteLine("Could not open the GitHub issues page: " + ex.Message);
        }
    }

    [RelayCommand]
    private void Continue() => RequestClose();

    [RelayCommand]
    private void Quit()
    {
        _onQuit();
        RequestClose();
    }
}
