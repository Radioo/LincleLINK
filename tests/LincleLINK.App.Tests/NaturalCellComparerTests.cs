using FluentAssertions;
using LincleLINK.App.Views;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// The DataGrid sort comparer: string cells use natural order, other cells fall
/// back to the default comparer.
/// </summary>
public sealed class NaturalCellComparerTests
{
    [Fact]
    public void String_cells_compare_naturally()
    {
        NaturalCellComparer.Instance.Compare("IIDX 10th style", "IIDX 9th style").Should().BeGreaterThan(0);
        NaturalCellComparer.Instance.Compare("IIDX 9th style", "IIDX 10th style").Should().BeLessThan(0);
        NaturalCellComparer.Instance.Compare("same", "same").Should().Be(0);
    }

    [Fact]
    public void Non_string_cells_use_the_default_comparer()
    {
        NaturalCellComparer.Instance.Compare(2, 10).Should().BeLessThan(0);
        NaturalCellComparer.Instance.Compare(10, 2).Should().BeGreaterThan(0);
    }
}
