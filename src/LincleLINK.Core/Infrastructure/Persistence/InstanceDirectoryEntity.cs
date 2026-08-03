namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// One <c>DirectoryList</c> row of an instance. <see cref="Ordinal"/> preserves the
/// manifest's array order so round-trips match the JSON schema exactly.
/// </summary>
public sealed class InstanceDirectoryEntity
{
    public long Id { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Value { get; set; } = string.Empty;
}
