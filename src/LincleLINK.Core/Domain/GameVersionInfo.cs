namespace LincleLINK.Core.Domain;

public enum DetectionConfidence
{
    None,
    DllOnly,
    Xml,
    XmlAndPe,
}

public sealed record GameVersionInfo(
    string GameCode,
    string GameTitle,
    string? Dest,
    string? Spec,
    string? Rev,
    string? DateCode,
    string? PeIdentifier,
    string? DisplayTitle,
    string? LogoKey,
    DetectionConfidence Confidence)
{
    public static GameVersionInfo None => new(
        string.Empty, string.Empty, null, null, null,
        null, null, null, null, DetectionConfidence.None);
}

public sealed record DetectionResult(
    GameVersionInfo? Info,
    string? GameRootPath,
    string? DataFolderName,
    bool IsGameRoot);
