using FluentAssertions;
using Xunit;

namespace LincleLINK.Core.Tests;

public sealed class ToolchainSmokeTests
{
    [Fact]
    public void Test_infrastructure_is_wired()
    {
        true.Should().BeTrue();
    }
}
