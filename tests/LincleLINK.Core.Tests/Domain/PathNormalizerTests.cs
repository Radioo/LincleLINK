using FluentAssertions;
using LincleLINK.Core.Domain;
using Xunit;

namespace LincleLINK.Core.Tests.Domain;

public sealed class PathNormalizerTests
{
    [Fact]
    public void ToPlatformSeparators_converts_both_separator_types()
    {
        var sep = Path.DirectorySeparatorChar;
        PathNormalizer.ToPlatformSeparators(@"sound\25063").Should().Be($"sound{sep}25063");
        PathNormalizer.ToPlatformSeparators("sound/25063").Should().Be($"sound{sep}25063");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a/b/c.txt")]
    public void IsSafeRelativePath_accepts_relative_paths(string path)
    {
        PathNormalizer.IsSafeRelativePath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../a")]
    [InlineData("a/../b")]
    [InlineData("/root")]
    [InlineData("\\root")]
    [InlineData("C:\\foo")]
    [InlineData("a/b:c")]
    public void IsSafeRelativePath_rejects_unsafe_paths(string path)
    {
        PathNormalizer.IsSafeRelativePath(path).Should().BeFalse();
    }

    [Fact]
    public void Canonicalize_matches_backslash_and_forward_slash_forms()
    {
        var backslash = PathNormalizer.Canonicalize(@"sound\25063\data.bin");
        var slash = PathNormalizer.Canonicalize("sound/25063/data.bin");
        backslash.Should().Be("sound/25063/data.bin");
        backslash.Should().Be(slash);
    }

    [Fact]
    public void Canonicalize_strips_leading_separators_and_empty_segments()
    {
        PathNormalizer.Canonicalize(@"/contents\data").Should().Be("contents/data");
    }
}
