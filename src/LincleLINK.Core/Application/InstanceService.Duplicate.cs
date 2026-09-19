using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
using Microsoft.Extensions.Logging;

namespace LincleLINK.Core.Application;

public sealed record DuplicateInstanceResult(bool Success, string? Error);

public sealed partial class InstanceService
{
    /// <summary>
    /// Duplicate (plan 16 D3): saves a new entry with the same files, directories,
    /// detected game and logo as an existing one. It copies no Storage content,
    /// since both entries point at the same hashed files. It still reads and writes
    /// every file row of the entry, so all of it runs on the thread pool and
    /// <paramref name="status"/> says which part is under way (CLAUDE.md: the UI
    /// never freezes, and everything that takes time shows progress).
    /// </summary>
    public Task<DuplicateInstanceResult> DuplicateInstanceAsync(
        string sourceName,
        string newName,
        IProgress<string>? status = null,
        CancellationToken ct = default)
        => Task.Run(() => DuplicateAsync(sourceName, newName, status, ct), ct);

    private async Task<DuplicateInstanceResult> DuplicateAsync(
        string sourceName,
        string newName,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var nameError = InstanceNameValidator.FirstError(newName);
        if (nameError is not null)
        {
            return new DuplicateInstanceResult(false, nameError);
        }

        status?.Report($"Reading {sourceName}...");
        if (await _repository.ExistsAsync(newName, ct))
        {
            return new DuplicateInstanceResult(false, "A library entry with this name already exists.");
        }

        var (source, notFound) = await InstanceLookup.GetAsync(_repository, sourceName, ct);
        if (source is null)
        {
            return new DuplicateInstanceResult(false, notFound);
        }

        var copy = Instance.Create(newName, source.FileList, source.DirectoryList);
        copy.DetectedGame = source.DetectedGame;
        copy.CustomLogoSource = source.CustomLogoSource;
        status?.Report($"Saving {newName} ({copy.TotalFileCount} files)...");
        await _repository.SaveAsync(copy, ct);

        LogInstanceDuplicated(sourceName, newName);
        return new DuplicateInstanceResult(true, null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicated instance '{SourceName}' as '{NewName}'")]
    private partial void LogInstanceDuplicated(string sourceName, string newName);
}
