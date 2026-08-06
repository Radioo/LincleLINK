using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Application;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// Forced one-time JSON → SQLite migration window (plan 13 §7). Non-cancellable:
/// the migration runs to completion (quarantining problem files) before the window
/// closes and the main window content loads. On a fatal failure the app still opens
/// with un-migrated JSON left on disk so the next launch re-offers.
/// </summary>
public partial class StorageMigrationViewModel : ViewModelBase
{
    private readonly StorageMigrationService _migration;
    private readonly ILogger<StorageMigrationViewModel> _logger;

    public override string Title => "Upgrading database";

    public override Size DialogSize => new(560, 440);

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private string _status = "Upgrading instance database…";

    [ObservableProperty]
    private double _progress;

    public event EventHandler<StorageMigrationResult>? Completed;

    public StorageMigrationViewModel(StorageMigrationService migration, ILogger<StorageMigrationViewModel> logger)
    {
        _migration = migration;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        try
        {
            var log = ProgressBridge.Create<string>(AddLogLine, batchSize: 100);
            var percent = ProgressBridge.Create<double>(p => Progress = p);
            var result = await Task.Run(() => _migration.MigrateAsync(log, percent));

            Status = result.Errors.Count == 0
                ? "Upgrade complete."
                : "Upgrade complete. Some manifests could not be read and were quarantined.";
            AddLogLine(
                $"Migrated {result.Migrated}, skipped {result.Skipped}, quarantined {result.Quarantined}.");

            Completed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            // Never brick the app: report, keep the un-migrated JSON on disk (the next
            // launch re-offers), and let the main window open regardless.
            Status = "Upgrade failed. Your existing data has been left untouched.";
            AddLogLine($"Upgrade failed: {ex.Message}");
            _logger.LogError(ex, "Storage migration failed");
        }
        finally
        {
            RequestClose();
        }
    }

    /// <summary>
    /// Appends a user-visible line to this window's activity feed with a timestamp
    /// prefix and mirrors it into the diagnostic log (issue #17 D4/D5).
    /// </summary>
    private void AddLogLine(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
        _logger.LogDebug("Activity: {Line}", line);
    }
}
