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
    public void Digit_free_names_fall_back_to_ordinal_order()
    {
        new[] { "charlie", "Alpha", "beta" }
            .Order(NaturalStringComparer.Instance)
            .Should().Equal("Alpha", "beta", "charlie");
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
