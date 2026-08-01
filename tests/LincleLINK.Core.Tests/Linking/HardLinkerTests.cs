using FluentAssertions;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Infrastructure.Linking;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Linking;

/// <summary>
/// Runs on the CI OS present (Windows or Linux); skipped elsewhere. The analyzer
/// needs explicit IsWindows/IsLinux guards to allow the platform-annotated impls.
/// </summary>
public sealed class HardLinkerTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IHardLinker? CreateLinker()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32HardLinker();
        }

        if (OperatingSystem.IsLinux())
        {
            return new UnixHardLinker();
        }

        return null;
    }

    [Fact]
    public void Create_link_produces_real_hard_link()
    {
        var linker = CreateLinker();
        if (linker is null)
        {
            return;
        }

        var source = _temp.CreateFile("src.txt", "hello"u8.ToArray());
        var target = Path.Combine(_temp.Root, "target.txt");

        linker.TryCreateLink(source, target, out var error).Should().BeTrue();

        File.Exists(target).Should().BeTrue();
        File.ReadAllText(target).Should().Be("hello");
    }

    [Fact]
    public void Deleting_source_keeps_target_intact()
    {
        var linker = CreateLinker();
        if (linker is null)
        {
            return;
        }

        var source = _temp.CreateFile("src.txt", "hello"u8.ToArray());
        var target = Path.Combine(_temp.Root, "target.txt");

        linker.TryCreateLink(source, target, out _).Should().BeTrue();
        File.Delete(source);

        File.Exists(target).Should().BeTrue();
        File.ReadAllText(target).Should().Be("hello");
    }

    [Fact]
    public void Missing_source_returns_false_with_error()
    {
        var linker = CreateLinker();
        if (linker is null)
        {
            return;
        }

        var result = linker.TryCreateLink(
            Path.Combine(_temp.Root, "missing.txt"),
            Path.Combine(_temp.Root, "t.txt"),
            out var error);

        result.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Existing_target_returns_false_with_error()
    {
        var linker = CreateLinker();
        if (linker is null)
        {
            return;
        }

        var source = _temp.CreateFile("src.txt", "hello"u8.ToArray());
        var target = _temp.CreateFile("target.txt", "existing"u8.ToArray());

        var result = linker.TryCreateLink(source, target, out var error);

        result.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }
}
