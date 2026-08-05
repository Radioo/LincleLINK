namespace LincleLINK.Core.Infrastructure.Collections;

/// <summary>
/// Case-insensitive string comparison that treats embedded ASCII digit runs as
/// numbers, so "IIDX 9th style" sorts before "IIDX 10th style" and before
/// "IIDX28". Non-numeric segments compare case-insensitively (matching the
/// <see cref="StringComparer.OrdinalIgnoreCase"/> semantics used for names
/// elsewhere); a digit run is compared by numeric value with leading zeros
/// ignored, using length first (shorter run is the smaller number, avoiding
/// overflow), then by value.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xi = 0;
        var yi = 0;

        while (xi < x.Length && yi < y.Length)
        {
            if (IsAsciiDigit(x[xi]) && IsAsciiDigit(y[yi]))
            {
                var xStart = xi;
                var yStart = yi;
                while (xi < x.Length && IsAsciiDigit(x[xi]))
                {
                    xi++;
                }

                while (yi < y.Length && IsAsciiDigit(y[yi]))
                {
                    yi++;
                }

                var xEnd = xi;
                var yEnd = yi;

                // Compare numeric values: leading zeros carry no weight ("007" is 7).
                var xSignificant = FirstSignificant(x, xStart, xEnd);
                var ySignificant = FirstSignificant(y, yStart, yEnd);

                var xLen = xEnd - xSignificant;
                var yLen = yEnd - ySignificant;

                if (xLen != yLen)
                {
                    return xLen < yLen ? -1 : 1;
                }

                for (var i = 0; i < xLen; i++)
                {
                    var xc = x[xSignificant + i];
                    var yc = y[ySignificant + i];
                    if (xc != yc)
                    {
                        return xc < yc ? -1 : 1;
                    }
                }
            }
            else
            {
                var xc = char.ToLowerInvariant(x[xi]);
                var yc = char.ToLowerInvariant(y[yi]);
                if (xc != yc)
                {
                    return xc < yc ? -1 : 1;
                }

                xi++;
                yi++;
            }
        }

        if (xi == x.Length && yi == y.Length)
        {
            return 0;
        }

        return xi == x.Length ? -1 : 1;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    /// <summary>Index of the first non-zero digit in the run, or the run's end when all zeros.</summary>
    private static int FirstSignificant(string value, int start, int end)
    {
        while (start < end && value[start] == '0')
        {
            start++;
        }

        return start;
    }
}
