using System.Text.Json.Serialization;

namespace LincleLINK.Core.Domain;

/// <summary>
/// An instance manifest: the list of files that make up a game install and their
/// location within the original data structure. JSON shape matches the v2 schema
/// exactly (property names + declaration order). Totals are denormalized persisted
/// fields, recomputed on save by the repository.
/// </summary>
public sealed class Instance
{
    [JsonPropertyName("Name")]
    public string InstanceName { get; set; } = string.Empty;
    public long TotalFileSize { get; set; }
    public int TotalFileCount { get; set; }
    public string TotalFileSizeString { get; set; } = string.Empty;
    public List<InstanceFile> FileList { get; set; } = [];
    public List<string> DirectoryList { get; set; } = [];

    /// <summary>
    /// Creates an instance computing totals and unique directory list from the
    /// given files and directories (mirrors the v2 constructor).
    /// </summary>
    public static Instance Create(
        string name,
        IEnumerable<InstanceFile> files,
        IEnumerable<string> directories)
    {
        var instance = new Instance
        {
            InstanceName = name,
            FileList = files.ToList(),
            DirectoryList = directories.ToList(),
        };
        instance.RecomputeTotals();
        return instance;
    }

    /// <summary>
    /// Recomputes the denormalized totals from <see cref="FileList"/> so the derived
    /// fields have a single definition in the Domain (used by <see cref="Create"/>
    /// and by the repository on save). Keeps <c>TotalFileSizeString</c> a persisted
    /// field to match the v2 JSON schema.
    /// </summary>
    public void RecomputeTotals()
    {
        TotalFileCount = FileList.Count;
        TotalFileSize = FileList.Sum(f => f.FileSize);
        TotalFileSizeString = SizeFormatter.Format(TotalFileSize);
    }
}
