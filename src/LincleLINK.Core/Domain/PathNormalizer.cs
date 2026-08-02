namespace LincleLINK.Core.Domain;

/// <summary>
/// Normalizes between the backslash paths persisted by v2 (and torrent '/'-separated
/// paths) and the host platform's separators, and guards path traversal.
/// </summary>
public static class PathNormalizer
{
    /// <summary>Converts stored backslash/slash paths to the host separator.</summary>
    public static string ToPlatformSeparators(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// Rejects rooted paths and any '.'/'..'/drive-letter segments so paths derived
    /// from stored or torrent data can be written safely. Empty/blank input is
    /// accepted: with no segments there is nothing to traverse.
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (path.Length == 0)
        {
            return true;
        }

        // Platform-independent check: normalize separators, then reject anything that
        // is rooted (a leading separator after normalization covers \root on Unix too),
        // or contains '.', '..' or a drive-letter segment.
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (segment == "." || segment == ".." || segment.Contains(':'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A neutral matching key: separators become '/', leading separators and empty
    /// segments are dropped. Used so an instance stored with '\' matches a
    /// '/'-separated torrent path on either OS.
    /// </summary>
    public static string Canonicalize(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', segments);
    }
}
