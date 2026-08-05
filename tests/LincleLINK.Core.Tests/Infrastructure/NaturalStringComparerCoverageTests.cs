using FluentAssertions;
using LincleLINK.Core.Infrastructure.Collections;
using Xunit;

namespace LincleLINK.Core.Tests.Infrastructure;

/// <summary>
/// Comparer branches the shared contract misses: non-interned equal strings and
/// equal digit runs that are followed by more differing text.
/// </summary>
public sealed class NaturalStringComparerCoverageTests
{
    [Fact]
    public void Equal_non_reference_strings_compare_zero()
    {
        var a = new string('x', 4);
        var b = new string('x', 4);
        a.Should().NotBeSameAs(b);

        NaturalStringComparer.Instance.Compare(a, b).Should().Be(0);
    }

    [Fact]
    public void Equal_digit_runs_then_more_text_keeps_comparing()
    {
        // Both have the digit run "1"; after it the rest differs, so the digit-run
        // block falls through and ordinary comparison resumes.
        NaturalStringComparer.Instance.Compare("a1b", "a1c").Should().BeLessThan(0);
        NaturalStringComparer.Instance.Compare("a1c", "a1b").Should().BeGreaterThan(0);
    }

    [Fact]
    public void Equal_digit_runs_and_equal_prefix_compare_zero()
    {
        var a = string.Concat("a", "1b");
        var b = string.Concat("a", "1b");
        NaturalStringComparer.Instance.Compare(a, b).Should().Be(0);
    }
}
