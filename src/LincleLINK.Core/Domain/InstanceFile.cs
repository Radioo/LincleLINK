namespace LincleLINK.Core.Domain;

/// <summary>
/// One file record inside an instance manifest. Value equality is used by torrent
/// file matching and tests. JSON shape matches the v2 schema exactly.
/// </summary>
public sealed record InstanceFile(
    string FileName,
    string RelativePath,
    long FileSize,
    string HashedFileName);
