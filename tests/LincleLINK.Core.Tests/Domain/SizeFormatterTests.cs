using FluentAssertions;
using LincleLINK.Core.Domain;
using Xunit;

namespace LincleLINK.Core.Tests.Domain;

public sealed class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]          // v2-bug regression: exact powers of 1024
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    [InlineData(463806, "452.94 KB")]
    public void Format_returns_expected(long size, string expected)
    {
        SizeFormatter.Format(size).Should().Be(expected);
    }

    [Fact]
    public void Format_negative_throws()
    {
        var act = () => SizeFormatter.Format(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
