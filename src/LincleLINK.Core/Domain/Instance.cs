namespace LincleLINK.Core.Domain;

/// <summary>
/// An instance manifest: the list of files that make up a game install and their
/// location within the original data structure. JSON shape matches the v2 schema
/// exactly (property names + declaration order). Totals are denormalized persisted
/// fields, recomputed on save by the repository.
/// </summary>
public sealed class Instance
{
    public string Name { get; set; } = string.Empty;
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
        var fileList = files.ToList();
        var directoryList = directories.ToList();

        long total = 0;
        foreach (var file in fileList)
        {
            total += file.FileSize;
        }

        return new Instance
        {
            Name = name,
            FileList = fileList,
            DirectoryList = directoryList,
            TotalFileSize = total,
            TotalFileCount = fileList.Count,
            TotalFileSizeString = SizeFormatter.Format(total),
        };
    }
}
