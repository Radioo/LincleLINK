using System.Text.RegularExpressions;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;

namespace LincleLINK.Core.Infrastructure.Storage;

public sealed class FileStore : IFileStore
{
    private static readonly Regex HashNamePattern =
        new(@"^[0-9A-F]{32}(\.[^\\/]+)?$", RegexOptions.CultureInvariant);

    private readonly IAppPaths _paths;
    private readonly IHardLinker _hardLinker;

    public FileStore(IAppPaths paths, IHardLinker hardLinker)
    {
        _paths = paths;
        _hardLinker = hardLinker;
    }

    public bool Exists(string hashedFileName)
    {
        ValidateHashName(hashedFileName);
        return File.Exists(GetPath(hashedFileName));
    }

    public string GetPath(string hashedFileName)
    {
        ValidateHashName(hashedFileName);
        return Path.Combine(_paths.DbDirectory, hashedFileName);
    }

    public async Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        if (Exists(hashedFileName))
        {
            return;
        }

        Directory.CreateDirectory(_paths.DbDirectory);
        await CopyFileAsync(sourcePath, GetPath(hashedFileName), ct);
    }

    public Task MoveToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        ct.ThrowIfCancellationRequested();

        if (Exists(hashedFileName))
        {
            // v2 dedup quirk: source is left in place, counted as "already exists".
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(_paths.DbDirectory);
        File.Move(sourcePath, GetPath(hashedFileName));
        return Task.CompletedTask;
    }

    public async Task CopyOutAsync(string hashedFileName, string destinationPath, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        if (File.Exists(destinationPath))
        {
            return;
        }

        await CopyFileAsync(GetPath(hashedFileName), destinationPath, ct);
    }

    public Task<bool> LinkOutAsync(string hashedFileName, string destinationPath, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_hardLinker.TryCreateLink(GetPath(hashedFileName), destinationPath, out _));
    }

    public Task DeleteAsync(string hashedFileName, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        ct.ThrowIfCancellationRequested();

        var path = GetPath(hashedFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(_paths.DbDirectory))
            {
                return [];
            }

            return Directory.GetFiles(_paths.DbDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f)!)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }, ct);

    public Task<long> GetTotalSizeAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(_paths.DbDirectory))
            {
                return 0L;
            }

            long total = 0;
            foreach (var file in Directory.GetFiles(_paths.DbDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                total += new FileInfo(file).Length;
            }

            return total;
        }, ct);

    private static void ValidateHashName(string hashedFileName)
    {
        if (!HashNamePattern.IsMatch(hashedFileName))
        {
            throw new ArgumentOutOfRangeException(nameof(hashedFileName), "Invalid hashed file name.");
        }
    }

    private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
    {
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var dst = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await src.CopyToAsync(dst, 81920, ct);
    }
}
