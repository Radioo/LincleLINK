namespace LincleLINK.Core.Abstractions.Linking;

/// <summary>
/// Platform hard-link abstraction. Per-file failures return false + a user-presentable
/// error instead of throwing, so callers can log and continue.
/// </summary>
public interface IHardLinker
{
    bool TryCreateLink(string sourcePath, string linkPath, out string? error);
}
