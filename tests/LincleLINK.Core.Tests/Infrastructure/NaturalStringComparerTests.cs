using FluentAssertions;
using LincleLINK.Core.Infrastructure.Collections;
using Xunit;

namespace LincleLINK.Core.Tests.Infrastructure;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void Sorts_embedded_numbers_numerically()
    {
        var names = new[]
        {
            "IIDX 34 ZINRAI",
            "IIDX 10th style",
            "IIDX 9th style",
            "IIDX 11 IIDX RED",
            "IIDX 20 tricoro",
        };

        var sorted = names.Order(NaturalStringComparer.Instance);

        sorted.Should().Equal(
            "IIDX 9th style",
            "IIDX 10th style",
            "IIDX 11 IIDX RED",
            "IIDX 20 tricoro",
            "IIDX 34 ZINRAI");
    }

    [Fact]
    public void Shorter_digit_run_is_the_smaller_number()
    {
        new[] { "item100", "item9", "item10" }
            .Order(NaturalStringComparer.Instance)
            .Should().Equal("item9", "item10", "item100");
    }

    [Fact]
    public void Digit_free_names_fall_back_to_case_insensitive_order()
    {
        new[] { "charlie", "Alpha", "beta" }
            .Order(NaturalStringComparer.Instance)
            .Should().Equal("Alpha", "beta", "charlie");
    }

    [Fact]
    public void Leading_zeros_are_ignored_numerically()
    {
        new[] { "item007", "item10", "item0070" }
            .Order(NaturalStringComparer.Instance)
            .Should().Equal("item007", "item10", "item0070");
    }

    [Fact]
    public void All_zero_run_sorts_as_zero()
    {
        NaturalStringComparer.Instance.Compare("item000", "item0").Should().Be(0);
        NaturalStringComparer.Instance.Compare("item000", "item1").Should().BeLessThan(0);
    }

    [Fact]
    public void Full_width_digits_are_compared_as_plain_characters()
    {
        // Full-width digits are not ASCII digits, so they must not enter the
        // numeric path (which would break transitivity and could make List.Sort
        // throw "comparer returned inconsistent results"). Compared as ordinary
        // characters, "９" (U+FF19) sorts above ASCII "10" and "a".
        NaturalStringComparer.Instance.Compare("９", "a").Should().BeGreaterThan(0);
        NaturalStringComparer.Instance.Compare("a", "１０").Should().BeLessThan(0);
    }

    [Fact]
    public void Case_insensitive_comparison()
    {
        NaturalStringComparer.Instance.Compare("iidx 9th style", "IIDX 9TH STYLE").Should().Be(0);
        new[] { "IIDX 10th style", "iidx 9th style" }
            .Order(NaturalStringComparer.Instance)
            .Should().Equal("iidx 9th style", "IIDX 10th style");
    }

    [Fact]
    public void Equal_names_compare_zero()
    {
        NaturalStringComparer.Instance.Compare("IIDX 28 BISTROVER", "IIDX 28 BISTROVER").Should().Be(0);
    }

    [Fact]
    public void Null_sorts_first()
    {
        NaturalStringComparer.Instance.Compare(null, "a").Should().BeLessThan(0);
        NaturalStringComparer.Instance.Compare("a", null).Should().BeGreaterThan(0);
        NaturalStringComparer.Instance.Compare(null, null).Should().Be(0);
    }

    [Fact]
    public void Prefix_of_another_name_sorts_first()
    {
        NaturalStringComparer.Instance.Compare("IIDX", "IIDX9").Should().BeLessThan(0);
    }
}
