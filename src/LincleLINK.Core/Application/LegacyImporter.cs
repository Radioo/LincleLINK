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

        var serializer = new XmlSerializer(typeof(DBInfo));
        await using var fs = File.OpenRead(xmlPath);
        var reader = XmlReader.Create(fs);
        var info = (DBInfo?)serializer.Deserialize(reader);

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
                var relativePath = fileInfo.Location;
                if (relativePath.StartsWith('\\'))
                {
                    relativePath = relativePath[1..];
                }

                dirs.Add(relativePath);
                files.Add(new InstanceFile(
                    fileInfo.OriginalFileName,
                    relativePath,
                    fileInfo.SizeBytes,
                    fileInfo.HashedFileName));
            }

            var instance = Instance.Create(legacy.InstanceName, files, dirs);
            await _repository.SaveAsync(instance, ct);
            imported.Add(legacy.InstanceName);
        }

        return new LegacyImportResult(imported, skipped);
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
        public string Location { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
