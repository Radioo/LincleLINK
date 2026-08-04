using FluentAssertions;
using LincleLINK.Core.Application;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// The <see cref="StatusSummary"/> projection record: string formatting for each
/// field and the storage-share ratio in both empty and non-empty states.
/// </summary>
public sealed class StatusSummaryTests
{
    [Fact]
    public void Strings_are_formatted_from_the_numeric_fields()
    {
        var summary = new StatusSummary(1024, 4096, 2048, 8192);

        summary.DbSizeString.Should().Be("1 KB");
        summary.LibrarySizeString.Should().Be("4 KB");
        summary.SavingsString.Should().Be("2 KB");
        summary.FreeSpaceString.Should().Be("8 KB");
    }

    [Fact]
    public void Negative_library_total_clamps_to_zero()
    {
        var summary = new StatusSummary(1024, -10, 0, 0);

        summary.LibrarySizeString.Should().Be("0 B");
    }

    [Fact]
    public void Storage_share_is_clamped_to_the_unit_interval()
    {
        var over = new StatusSummary(DbSize: 200, InstancesTotalSize: 100, Savings: 0, FreeSpace: 0);

        over.StorageShare.Should().Be(1);
    }

    [Fact]
    public void Storage_share_is_zero_for_an_empty_library()
    {
        var empty = new StatusSummary(DbSize: 500, InstancesTotalSize: 0, Savings: 0, FreeSpace: 0);

        empty.StorageShare.Should().Be(0);
    }

    [Fact]
    public void Storage_share_reflects_the_actual_ratio()
    {
        var summary = new StatusSummary(DbSize: 25, InstancesTotalSize: 100, Savings: 0, FreeSpace: 0);

        summary.StorageShare.Should().Be(0.25);
    }
}
