using System.Collections.ObjectModel;
using Avalonia;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// The "Add folder to library" dialog (plan 14 §2): name + folder, then two
/// description radio cards - Reclaim space (recommended) and Keep originals.
/// Picking a folder kicks off a background analysis that estimates its size and
/// pre-flights hard-linkability, so a cross-volume folder disables the Reclaim
/// card with an inline explanation instead of failing later.
/// </summary>
public partial class AddInstanceViewModel : ViewModelBase
{
    private readonly InstanceService _service;
    private readonly IDialogService _dialogs;
    private readonly ITaskbarProgress _taskbarProgress;
    private readonly IFileSystem _fileSystem;
    private readonly IHardLinkPreflight _preflight;
    private readonly ILogger<AddInstanceViewModel> _logger;

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>
    /// True once an add completed and requested close - lets the hosting shell
    /// distinguish success from a user-dismissed panel (plan 15 D4).
    /// </summary>
    public bool CompletedSuccessfully { get; private set; }

    public override string Title => "Add folder to library";

    public override Size DialogSize => new(560, 640);

    public override Size DialogMinSize => new(480, 540);

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _dataPath = string.Empty;

    /// <summary>Reclaim space (move) - the recommended default.</summary>
    [ObservableProperty]
    private bool _isReclaimChecked = true;

    [ObservableProperty]
    private bool _isKeepChecked;

    /// <summary>False when the folder is on a different volume than storage.</summary>
    [ObservableProperty]
    private bool _reclaimAvailable = true;

    /// <summary>The pre-flight reason shown inside the Reclaim card when unavailable.</summary>
    [ObservableProperty]
    private string _crossVolumeReason = string.Empty;

    /// <summary>Formatted folder size ("14.2 GB"), or empty while unknown.</summary>
    [ObservableProperty]
    private string _estimatedSizeText = string.Empty;

    [ObservableProperty]
    private bool _isCalculatingSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(BrowseCommand), nameof(CreateInstanceCommand), nameof(CancelOperationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    /// <summary>Transient per-file status line (plan 14 D5).</summary>
    [ObservableProperty]
    private string _statusLine = string.Empty;

    /// <summary>
    /// Worker count for parallel hashing (1..ProcessorCount), injected by the
    /// caller (<see cref="MainViewModel.OpenAddInstanceAsync"/>) from settings.
    /// </summary>
    [ObservableProperty]
    private int _threadCount = Environment.ProcessorCount;

    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource? _operationCts;

    public AddInstanceViewModel(
        InstanceService service,
        IDialogService dialogs,
        ITaskbarProgress taskbarProgress,
        IFileSystem fileSystem,
        IHardLinkPreflight preflight,
        ILogger<AddInstanceViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _taskbarProgress = taskbarProgress;
        _fileSystem = fileSystem;
        _preflight = preflight;
        _logger = logger;
    }

    public CopyMoveMode Mode => IsReclaimChecked ? CopyMoveMode.Move : CopyMoveMode.Copy;

    // Keep the radio booleans mutually exclusive at the VM level too, so
    // programmatic changes (cross-volume fallback, tests) behave like UI clicks.
    partial void OnIsReclaimCheckedChanged(bool value)
    {
        if (value)
        {
            IsKeepChecked = false;
        }
    }

    partial void OnIsKeepCheckedChanged(bool value)
    {
        if (value)
        {
            IsReclaimChecked = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.PickFolderAsync("Select the folder to add");
        if (path is not null)
        {
            DataPath = path;
        }
    }

    partial void OnDataPathChanged(string value) => _ = AnalyzeFolderAsync(value);

    /// <summary>
    /// Background folder analysis: hard-link pre-flight first (cheap, drives the
    /// Reclaim card availability), then the size estimate (can take a while on
    /// large trees). Superseded analyses are cancelled; results from a stale run
    /// are dropped.
    /// </summary>
    private async Task AnalyzeFolderAsync(string path)
    {
        _analysisCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analysisCts = cts;

        EstimatedSizeText = string.Empty;
        CrossVolumeReason = string.Empty;
        ReclaimAvailable = true;
        IsCalculatingSize = false;

        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.DirectoryExists(path))
        {
            return;
        }

        try
        {
            IsCalculatingSize = true;

            var reason = await Task.Run(() => _preflight.CheckLinkTo(path), cts.Token);
            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                ReclaimAvailable = false;
                CrossVolumeReason = reason;
                if (IsReclaimChecked)
                {
                    IsKeepChecked = true;
                }
            }

            var total = await Task.Run(() =>
            {
                long sum = 0;
                foreach (var file in _fileSystem.EnumerateFiles(path, recursive: true))
                {
                    cts.Token.ThrowIfCancellationRequested();
                    sum += _fileSystem.GetFileLength(file);
                }

                return sum;
            }, cts.Token);

            if (!cts.Token.IsCancellationRequested)
            {
                EstimatedSizeText = SizeFormatter.Format(total);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer analysis took over; nothing to report.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Estimation is advisory only; the add operation will surface real errors.
        }
        finally
        {
            if (_analysisCts == cts)
            {
                IsCalculatingSize = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task CreateInstanceAsync()
    {
        IsBusy = true;
        _logger.LogInformation("Starting add-instance for '{InstanceName}'", InstanceName);
        _taskbarProgress.BeginOperation();
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        CancelOperationCommand.NotifyCanExecuteChanged();
        var log = ProgressBridge.Create<string>(AddLogLine, batchSize: 100);
        var status = ProgressBridge.Create<string>(line => StatusLine = line, batchSize: 200);
        var percent = ProgressBridge.Create<double>(p =>
        {
            Progress = p;
            _taskbarProgress.Report(p);
        });

        try
        {
            var request = new AddInstanceRequest(
                InstanceName, DataPath, Mode, Math.Clamp(ThreadCount, 1, Environment.ProcessorCount));
            var result = await _service.CreateInstanceAsync(request, log, percent, status, cts.Token);

            if (result.Success)
            {
                _logger.LogInformation("Add-instance for '{InstanceName}' completed", InstanceName);
                CompletedSuccessfully = true;
                RequestClose();
            }
            else if (result.Error is not null)
            {
                _logger.LogWarning("Add-instance for '{InstanceName}' failed: {Error}", InstanceName, result.Error);
                await _dialogs.ErrorAsync(result.Error, "Add folder to library");
            }
            else
            {
                AddLogLine("Operation cancelled.");
            }
        }
        catch (OperationCanceledException)
        {
            AddLogLine("Operation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add-instance for '{InstanceName}' failed", InstanceName);
            await _dialogs.ErrorAsync(ex.Message, "Add folder to library");
        }
        finally
        {
            _operationCts = null;
            IsBusy = false;
            StatusLine = string.Empty;
            Progress = 0;
            _taskbarProgress.EndOperation();
        }
    }

    /// <summary>
    /// Appends a user-visible line to this panel's activity feed with a timestamp
    /// prefix and mirrors it into the diagnostic log (issue #17 D4/D5).
    /// </summary>
    private void AddLogLine(string line)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
        _logger.LogDebug("Activity: {Line}", line);
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        StatusLine = "Cancelling...";
    }

    /// <summary>Dismisses the hosting panel (disabled while an add is running).</summary>
    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Close() => RequestClose();

    private bool CanInteract() => !IsBusy;

    private bool CanCancelOperation() => IsBusy && _operationCts is not null;
}
