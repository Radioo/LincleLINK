namespace LincleLINK.App.Services;

/// <summary>
/// Storm and re-entrancy protection for the error-report window (issue #16 D4).
/// Plain and UI-free so the dedupe/cap logic is unit-testable. An exception is
/// identified by its type + message + top stack frame: an identical repeat bumps
/// the primary occurrence count, a different one becomes an additional section,
/// and past <see cref="MaxDistinctExceptions"/> only an overflow counter grows.
/// </summary>
public sealed class ExceptionAccumulator
{
    /// <summary>At most this many distinct exceptions accumulate per report window.</summary>
    public const int MaxDistinctExceptions = 10;

    private sealed record Accumulated(Exception Exception, string Key)
    {
        public int Count { get; set; } = 1;
    }

    private readonly List<Accumulated> _items = [];
    private int _overflowCount;

    public ExceptionAccumulator()
    {
    }

    /// <summary>The first (headline) exception; null before any add.</summary>
    public Exception? Primary => _items.Count > 0 ? _items[0].Exception : null;

    /// <summary>How many times the primary exception occurred (drives the "×N" badge).</summary>
    public int PrimaryOccurrences => _items.Count > 0 ? _items[0].Count : 0;

    public int DistinctCount => _items.Count;

    public int OverflowCount => _overflowCount;

    public bool HasOverflow => _overflowCount > 0;

    /// <summary>The overflow line, singular for exactly one dropped error.</summary>
    public static string FormatOverflow(int count)
        => count == 1 ? "…and 1 more distinct error" : $"…and {count} more distinct errors";

    public void Reset()
    {
        _items.Clear();
        _overflowCount = 0;
    }

    /// <summary>
    /// Registers another occurrence. Returns false when it was dropped for hitting
    /// the cap (the overflow counter still grows).
    /// </summary>
    public bool Add(Exception exception)
    {
        var key = BuildKey(exception);
        var existing = _items.FirstOrDefault(item => item.Key == key);
        if (existing is not null)
        {
            existing.Count++;
            return true;
        }

        if (_items.Count >= MaxDistinctExceptions)
        {
            _overflowCount++;
            return false;
        }

        _items.Add(new Accumulated(exception, key));
        return true;
    }

    /// <summary>All distinct exceptions except the primary, for the appended sections.</summary>
    public IEnumerable<Exception> AdditionalExceptions() => _items.Skip(1).Select(item => item.Exception);

    /// <summary>
    /// The details text shown in the report window: the primary trace, then an
    /// "Also occurred" section per additional distinct exception, then the
    /// overflow line when the cap was reached.
    /// </summary>
    public string BuildDetailsText()
    {
        var sb = new System.Text.StringBuilder();
        if (Primary is not null)
        {
            sb.AppendLine(Primary.ToString());
        }

        foreach (var extra in AdditionalExceptions())
        {
            sb.AppendLine();
            sb.AppendLine("─ Also occurred ─");
            sb.AppendLine(extra.ToString());
        }

        if (HasOverflow)
        {
            sb.AppendLine();
            sb.AppendLine($"{FormatOverflow(OverflowCount)} (the {MaxDistinctExceptions}-exception cap was reached - further exceptions were dropped)");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Dedupe key for one exception: type + message + top stack frame. The frame
    /// anchors the identity to where the throw happened, not just its text.
    /// </summary>
    public static string BuildKey(Exception exception)
    {
        var frame = exception.StackTrace?
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
        return $"{exception.GetType().FullName}|{exception.Message}|{frame}";
    }
}
