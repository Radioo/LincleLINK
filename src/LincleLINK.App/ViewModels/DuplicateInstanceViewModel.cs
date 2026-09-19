using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain.Validation;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// Duplicate dialog (plan 16 D3): asks for the new entry's name, validated as the
/// user types with the same rules as Add.
/// </summary>
public partial class DuplicateInstanceViewModel : ViewModelBase
{
    private readonly InstanceService _service;
    private readonly ILogger<DuplicateInstanceViewModel> _logger;

    public DuplicateInstanceViewModel(InstanceService service, ILogger<DuplicateInstanceViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    [ObservableProperty]
    private string _sourceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    private string _newName = string.Empty;

    /// <summary>Why the current name can't be used; empty when it can.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    private string _error = string.Empty;

    /// <summary>True while the copy is being saved; the dialog can't close or start a second save meanwhile.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(CloseCommand))]
    private bool _isBusy;

    /// <summary>What the running duplicate is doing right now; empty when idle.</summary>
    [ObservableProperty]
    private string _statusLine = string.Empty;

    /// <summary>Name of the entry this dialog created; null when it closed without one.</summary>
    public string? CreatedName { get; private set; }

    public void Start(string sourceName)
    {
        SourceName = sourceName;
        NewName = $"{sourceName} - copy";
    }

    partial void OnNewNameChanged(string value)
        => Error = InstanceNameValidator.FirstError(value) ?? string.Empty;

    [RelayCommand(CanExecute = nameof(CanDuplicate))]
    private async Task DuplicateAsync()
    {
        IsBusy = true;
        StatusLine = $"Duplicating {SourceName}...";
        var status = ProgressBridge.Create<string>(line => StatusLine = line);
        try
        {
            var result = await _service.DuplicateInstanceAsync(SourceName, NewName, status);
            if (!result.Success)
            {
                _logger.LogInformation("Duplicate of '{SourceName}' refused: {Error}", SourceName, result.Error);
                Error = result.Error ?? string.Empty;
                return;
            }

            CreatedName = NewName;
        }
        catch (Exception ex)
        {
            // A locked or failing metadata DB keeps the dialog open with the reason,
            // so the user can retry instead of losing the typed name.
            _logger.LogError(ex, "Duplicate of '{SourceName}' failed", SourceName);
            Error = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
            StatusLine = string.Empty;
        }

        RequestClose();
    }

    private bool CanDuplicate() => !IsBusy && Error.Length == 0;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => RequestClose();

    private bool CanClose() => !IsBusy;
}
