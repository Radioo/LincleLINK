using System.Xml;
using System.Xml.Serialization;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

public sealed record LegacyImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> SkippedExisting);

/// <summary>
/// Imports instances from a v1 <c>DBInfo.xml</c> manifest. Behavior ported from v2
/// <c>ImportLegacyInstances</c>: strip a single leading backslash from the stored
/// location, derive the directory list from file paths, skip existing names
/// (case-insensitive). Totals are recomputed via <see cref="Instance.Create"/>.
/// </summary>
public sealed class LegacyImporter
{
    private readonly IInstanceRepository _repository;

    public LegacyImporter(IInstanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<LegacyImportResult> ImportAsync(string xmlPath, CancellationToken ct = default)
    {
        var imported = new List<string>();
        var skipped = new List<string>();

        DBInfo? info;
        try
        {
            var serializer = new XmlSerializer(typeof(DBInfo));
            await using var fs = File.OpenRead(xmlPath);
            var reader = XmlReader.Create(fs);
            info = (DBInfo?)serializer.Deserialize(reader);
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException or IOException or UnauthorizedAccessException)
        {
            // Parse failures are user-presentable (mirrors TorrentService.LoadAsync
            // converting expected parse failures into a clear message).
            throw new LegacyImportException(
                $"'{xmlPath}' is not a valid v1 DBInfo.xml: {ex.Message}", ex);
        }

        if (info?.InstanceList is null)
        {
            return new LegacyImportResult(imported, skipped);
        }

        foreach (var legacy in info.InstanceList)
        {
            if (await _repository.ExistsAsync(legacy.InstanceName, ct))
            {
                skipped.Add(legacy.InstanceName);
                continue;
            }

            var files = new List<InstanceFile>(legacy.InstanceFiles.Count);
            var dirs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fileInfo in legacy.InstanceFiles)
            {
                // A nil Location carries no directory info; skip rather than guess.
                if (TryBuild(fileInfo, out var file))
                {
                    dirs.Add(file.RelativePath);
                    files.Add(file);
                }
            }

            var instance = Instance.Create(legacy.InstanceName, files, dirs);
            await _repository.SaveAsync(instance, ct);
            imported.Add(legacy.InstanceName);
        }

        return new LegacyImportResult(imported, skipped);
    }

    /// <summary>
    /// Builds an <see cref="InstanceFile"/> from a legacy file entry, returning
    /// false when the entry carries no directory info (nil location) or a path or
    /// name that could escape the instance's own directory when LinkingService
    /// later materializes it. Extracted so each guard is unit-testable.
    /// </summary>
    internal static bool TryBuild(InstanceFileInfo fileInfo, out InstanceFile file)
    {
        file = default!;

        if (fileInfo.Location is null)
        {
            return false;
        }

        var relativePath = fileInfo.Location;
        if (relativePath.StartsWith('\\'))
        {
            relativePath = relativePath[1..];
        }

        if (!PathNormalizer.IsSafeRelativePath(relativePath)
            || string.IsNullOrWhiteSpace(fileInfo.OriginalFileName)
            || fileInfo.OriginalFileName.Contains(Path.DirectorySeparatorChar)
            || fileInfo.OriginalFileName.Contains('/')
            || fileInfo.OriginalFileName.Contains('\\'))
        {
            return false;
        }

        file = new InstanceFile(
            fileInfo.OriginalFileName,
            relativePath,
            fileInfo.SizeBytes,
            fileInfo.HashedFileName);
        return true;
    }

    [XmlRoot("DBInfo")]
    public sealed class DBInfo
    {
        public List<DataInstance> InstanceList { get; set; } = [];
    }
    public sealed class DataInstance
    {
        public string InstanceName { get; set; } = string.Empty;
        public List<InstanceFileInfo> InstanceFiles { get; set; } = [];
        public int Entries { get; set; }
        public long Size { get; set; }
    }

    public sealed class InstanceFileInfo
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string HashedFileName { get; set; } = string.Empty;

        /// <summary>
        /// Directory path stored in v1; nullable because <c>xsi:nil</c> can make an
        /// XmlSerializer string null even when declared non-nullable.
        /// </summary>
        public string? Location { get; set; }
        public long SizeBytes { get; set; }
    }
}

/// <summary>
/// Thrown when a legacy <c>DBInfo.xml</c> cannot be opened or parsed. The message
/// includes the file path so the UI can show a clear error instead of a raw
/// <see cref="System.Xml.XmlException"/> (mirrors <c>TorrentService</c>'s typed
/// parse-failure exception).
/// </summary>
public sealed class LegacyImportException : Exception
{
    public LegacyImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
