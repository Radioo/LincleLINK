namespace LincleLINK.Core.Domain;

/// <summary>
/// Lightweight projection of an instance for list views (DataGrid row) and summaries.
/// </summary>
public sealed record InstanceListEntry(
    string InstanceName,
    int FileCount,
    long TotalFileSize,
    string TotalFileSizeString,
    string? NameKey = null)
{
    public GameVersionInfo? DetectedGame { get; init; }
    public string? CustomLogoSource { get; init; }
    public string? LogoUri { get; init; }

    public static InstanceListEntry From(Instance instance) =>
        new(instance.InstanceName, instance.FileList.Count, instance.TotalFileSize, instance.TotalFileSizeString)
        {
            DetectedGame = instance.DetectedGame,
            CustomLogoSource = instance.CustomLogoSource,
        };
}
