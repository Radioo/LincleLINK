namespace LincleLINK.Core.Abstractions.Linking;

/// <summary>
/// Answers "can storage hard-link into this directory?" before an operation
/// starts, so a cross-volume target fails once with one clear message instead of
/// once per file (plan 14 D2). Inconclusive probes report success and let the
/// real operation surface its own errors.
/// </summary>
public interface IHardLinkPreflight
{
    /// <summary>
    /// Returns null when hard-linking from storage into
    /// <paramref name="directory"/> should work; otherwise a user-presentable
    /// reason (e.g. the directory is on a different drive than storage).
    /// Blocks on file IO; call off the UI thread.
    /// </summary>
    string? CheckLinkTo(string directory);
}
