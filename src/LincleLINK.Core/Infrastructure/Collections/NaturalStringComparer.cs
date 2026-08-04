namespace LincleLINK.Core.Infrastructure.Collections;

/// <summary>
/// Case-sensitive, ordinal string comparison that treats embedded digit runs as
/// numbers, so "IIDX 9th style" sorts before "IIDX 10th style" and before
/// "IIDX28". Non-numeric segments compare exactly as
/// <see cref="StringComparer.Ordinal"/>; a digit run is compared by length
/// first (shorter run is the smaller number, avoiding overflow), then by value.
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
            if (char.IsDigit(x[xi]) && char.IsDigit(y[yi]))
            {
                var xStart = xi;
                var yStart = yi;
                while (xi < x.Length && char.IsDigit(x[xi]))
                {
                    xi++;
                }

                while (yi < y.Length && char.IsDigit(y[yi]))
                {
                    yi++;
                }

                var xLen = xi - xStart;
                var yLen = yi - yStart;

                if (xLen != yLen)
                {
                    return xLen < yLen ? -1 : 1;
                }

                for (var i = 0; i < xLen; i++)
                {
                    var xc = x[xStart + i];
                    var yc = y[yStart + i];
                    if (xc != yc)
                    {
                        return xc < yc ? -1 : 1;
                    }
                }
            }
            else
            {
                var xc = x[xi];
                var yc = y[yi];
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
}
