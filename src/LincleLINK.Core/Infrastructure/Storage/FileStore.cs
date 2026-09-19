using System.Text.RegularExpressions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;

namespace LincleLINK.Core.Infrastructure.Storage;

public sealed class FileStore : IFileStore
{
    private static readonly Regex HashNamePattern =
        new(@"^[0-9A-F]{32}(\.[^\\/]+)?$", RegexOptions.CultureInvariant);

    // A copy streams into a temp file and takes its real name only once it is
    // complete. The temp name starts with a word, so it can never match the hash
    // pattern above: "<hash>.2dx.<guid>.tmp" would, since an extension may hold dots.
    private const string TempPrefix = "incoming-";
    private const string TempExtension = ".lincletmp";

    private readonly IAppPaths _paths;

    /// <summary>
    /// The once-per-session sweep of temp files that a crash left in <c>db/</c>.
    /// Lazy so that two first copies racing each other share one sweep: a second
    /// one could otherwise start after the first copy's temp file exists and take it.
    /// </summary>
    private readonly Lazy<Task> _sweep;

    public FileStore(IAppPaths paths)
    {
        _paths = paths;
        _sweep = new Lazy<Task>(SweepQuietlyAsync);
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

    public long GetSize(string hashedFileName)
    {
        ValidateHashName(hashedFileName);
        var info = new FileInfo(GetPath(hashedFileName));
        return info.Exists ? info.Length : 0;
    }

    public async Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        if (Exists(hashedFileName))
        {
            return;
        }

        Directory.CreateDirectory(_paths.DbDirectory);

        // Before this session's first temp file exists, so the sweep can't take
        // one of our own. Every copy awaits the same sweep.
        await _sweep.Value;
        await CopyFileAsync(sourcePath, GetPath(hashedFileName), ct);
    }

    /// <summary>Housekeeping must never fail a copy, and a faulted task here would fail every later one.</summary>
    private async Task SweepQuietlyAsync()
    {
        try
        {
            await DeleteLeftoverTempFilesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Every exception, not only the I/O ones: every copy of the session awaits
            // this one task, so a path the platform rejects (NotSupportedException,
            // say) would otherwise fail them all. The leftovers stay for the next
            // session or the next storage cleanup.
        }
    }

    public async Task CopyFromStoreAsync(string hashedFileName, string destinationPath, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        if (File.Exists(destinationPath))
        {
            return;
        }

        // Same shape as a copy into Storage: an export skips files that exist, so a
        // partial one from a cancelled run would stay truncated on every rerun.
        await CopyFileAsync(GetPath(hashedFileName), destinationPath, ct);
    }

    public Task DeleteAsync(string hashedFileName, CancellationToken ct = default)
    {
        ValidateHashName(hashedFileName);
        ct.ThrowIfCancellationRequested();

        var path = GetPath(hashedFileName);
        if (!File.Exists(path))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(path);
        }, ct);
    }

    public Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(_paths.DbDirectory))
            {
                return [];
            }

            // Only stored content. db/ can hold other files: a temp file of a copy
            // that crashed, a link preflight probe, something the user put there.
            // Every name returned here goes back into GetSize and DeleteAsync, which
            // reject anything that is not a hash name.
            //
            // Path.GetFileName returns null only for a trailing-separator path,
            // impossible here since every element comes from Directory.GetFiles.
            return Directory.GetFiles(_paths.DbDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f)!)
                .Where(name => HashNamePattern.IsMatch(name))
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

    public Task<int> DeleteLeftoverTempFilesAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(_paths.DbDirectory))
            {
                return 0;
            }

            var deleted = 0;
            foreach (var file in Directory.EnumerateFiles(
                         _paths.DbDirectory, $"{TempPrefix}*{TempExtension}", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (TryDelete(file))
                {
                    deleted++;
                }
            }

            return deleted;
        }, ct);

    private static void ValidateHashName(string hashedFileName)
    {
        if (!HashNamePattern.IsMatch(hashedFileName))
        {
            throw new ArgumentOutOfRangeException(nameof(hashedFileName), "Invalid hashed file name.");
        }
    }

    internal static string TempFileName() => $"{TempPrefix}{Guid.NewGuid():N}{TempExtension}";

    private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
    {
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await CopyAtomicallyAsync(src, dest, ct);
    }

    /// <summary>
    /// Streams <paramref name="source"/> into a temp file next to the destination
    /// and moves it into place only when every byte is there. A file under a hash
    /// name is trusted to be that content by every later add, so a cancelled or
    /// failed copy must leave nothing under it. Never replaces an existing file.
    /// </summary>
    internal static async Task CopyAtomicallyAsync(Stream source, string destinationPath, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(destinationPath) ?? string.Empty, TempFileName());
        try
        {
            await using (var dst = new FileStream(
                             tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(dst, 81920, ct);
            }

            try
            {
                File.Move(tempPath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another copy got there first. In Storage that is the same content
                // under the same hash; for an export the rule is to never overwrite.
                TryDelete(tempPath);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Best effort: a temp file that can't go now is swept by a later session.</summary>
    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
