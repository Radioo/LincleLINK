using FluentAssertions;
using LincleLINK.Core.Infrastructure.Linking;
using Xunit;

#pragma warning disable CA1416 // error mapping is platform-independent and intentionally tested everywhere

namespace LincleLINK.Core.Tests.Linking;

/// <summary>
/// The errno / Win32 error-to-message mappings (extracted so every platform's
/// error table is covered on any host OS).
/// </summary>
public sealed class HardLinkerErrorMappingTests
{
    [Theory]
    [InlineData(1, "Operation not permitted")]
    [InlineData(2, "Could not find the source file in storage")]
    [InlineData(17, "already exists at the target")]
    [InlineData(18, "different filesystem")]
    [InlineData(31, "Too many hard links")]
    [InlineData(99, "errno 99")]
    public void UnixHardLinker_maps_errno_to_messages(int errno, string expectedFragment)
    {
        UnixHardLinker.DescribeError(errno).Should().Contain(expectedFragment);
    }

    [Fact]
    public void UnixHardLinker_success_returns_null_error()
    {
        var linker = new UnixHardLinker((_, _) => 0);

        var ok = linker.TryCreateLink("/src", "/link", out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void UnixHardLinker_failure_reports_an_error()
    {
        var linker = new UnixHardLinker((_, _) => 2);

        var ok = linker.TryCreateLink("/src", "/link", out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(2, "Could not find the source file in storage")]
    [InlineData(3, "Could not find the source file in storage")]
    [InlineData(5, "Access denied")]
    [InlineData(17, "different drive")]
    [InlineData(80, "already exists at the target")]
    [InlineData(183, "already exists at the target")]
    [InlineData(1142, "Too many hard links")]
    [InlineData(1200, "Win32 error 1200")]
    public void Win32HardLinker_maps_error_codes_to_messages(int code, string expectedFragment)
    {
        Win32HardLinker.DescribeError(code).Should().Contain(expectedFragment);
    }
}
