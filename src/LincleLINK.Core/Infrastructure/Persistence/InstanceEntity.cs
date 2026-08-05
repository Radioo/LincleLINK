namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// SQLite row for an instance manifest. Mirrors the Domain <c>Instance</c> totals
/// plus a normalized <see cref="NameKey"/> that provides case-insensitive
/// uniqueness (matches the JSON repository's <c>OrdinalIgnoreCase</c> contract on
/// every platform, which SQLite <c>NOCASE</c> alone cannot guarantee).
/// </summary>
public sealed class InstanceEntity
{
    public string InstanceName { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public long TotalFileSize { get; set; }
    public int TotalFileCount { get; set; }
    public string TotalFileSizeString { get; set; } = string.Empty;
    public string? GameCode { get; set; }
    public string? GameTitle { get; set; }
    public string? DateCode { get; set; }
    public string? PeIdentifier { get; set; }
    public string? DisplayTitle { get; set; }
    public string? LogoKey { get; set; }
    public int? Confidence { get; set; }
    public string? CustomLogoSource { get; set; }
    public List<InstanceFileEntity> Files { get; set; } = [];
    public List<InstanceDirectoryEntity> Directories { get; set; } = [];
}
