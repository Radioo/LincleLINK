namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// One <c>FileList</c> row of an instance. <see cref="Ordinal"/> preserves the
/// manifest's array order so round-trips match the JSON schema exactly.
/// </summary>
public sealed class InstanceFileEntity
{
    public long Id { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string HashedFileName { get; set; } = string.Empty;
}
