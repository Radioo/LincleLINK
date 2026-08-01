namespace LincleLINK.Core.Domain;

public static class SizeFormatter
{
    /// <summary>
    /// Formats a byte count in a human-readable form (B / KB / MB / GB / TB), two
    /// decimals for units above B. Corrects v2's boundary bug where exact powers of
    /// 1024 (e.g. 1024 B) fell through to the TB branch.
    /// </summary>
    public static string Format(long size)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Size cannot be negative.");
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size} {units[unitIndex]}"
            : $"{Math.Round(value, 2)} {units[unitIndex]}";
    }
}
