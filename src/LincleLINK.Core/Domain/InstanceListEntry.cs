namespace LincleLINK.Core.Domain;

/// <summary>
/// Lightweight projection of an instance for list views (DataGrid row) and summaries.
/// </summary>
public sealed record InstanceListEntry(
    string InstanceName,
    int FileCount,
    long TotalFileSize,
    string TotalFileSizeString)
{
    public static InstanceListEntry From(Instance instance) =>
        new(instance.Name, instance.FileList.Count, instance.TotalFileSize, instance.TotalFileSizeString);
}
