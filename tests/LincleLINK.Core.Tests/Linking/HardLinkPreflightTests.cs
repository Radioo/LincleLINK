using FluentAssertions;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Infrastructure.Linking;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Linking;

/// <summary>
/// Probe lifecycle and inconclusive branches of <see cref="HardLinkPreflight"/>.
/// </summary>
public sealed class HardLinkPreflightTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();

    public void Dispose() => _temp.Dispose();

    private HardLinkPreflight CreatePreflight() => new(_paths, _hardLinker);

    [Fact]
    public void Missing_target_directory_is_inconclusive()
    {
        var result = CreatePreflight().CheckLinkTo(Path.Combine(_temp.Root, "missing"));

        result.Should().BeNull();
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public void Successful_probe_returns_null_and_cleans_up()
    {
        _paths.DbDirectory.Returns(Path.Combine(_temp.Root, "db"));
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });
        var target = Directory.CreateDirectory(Path.Combine(_temp.Root, "target")).FullName;

        var result = CreatePreflight().CheckLinkTo(target);

        result.Should().BeNull();
        _hardLinker.Received(1).TryCreateLink(
            Arg.Is<string>(p => p != null && p.Contains("preflight-")),
            Arg.Is<string>(p => p != null && p.Contains(".lincle-preflight-")),
            out _);
        Directory.GetFiles(_paths.DbDirectory).Should().BeEmpty();
    }

    [Fact]
    public void Failed_probe_returns_linker_error()
    {
        _paths.DbDirectory.Returns(Path.Combine(_temp.Root, "db"));
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = "cross-volume";
            return false;
        });
        var target = Directory.CreateDirectory(Path.Combine(_temp.Root, "target")).FullName;

        var result = CreatePreflight().CheckLinkTo(target);

        result.Should().Be("cross-volume");
    }

    [Fact]
    public void Probe_creation_failure_is_inconclusive()
    {
        // Occupy the db directory path with a file so the probe write throws.
        _paths.DbDirectory.Returns(Path.Combine(_temp.Root, "db"));
        File.WriteAllText(_paths.DbDirectory, "not a directory");
        var target = Directory.CreateDirectory(Path.Combine(_temp.Root, "target")).FullName;

        var result = CreatePreflight().CheckLinkTo(target);

        result.Should().BeNull();
    }

    [Fact]
    public void TryDelete_swallows_unauthorized_deletes()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Read-only file deletion only throws on Windows.");
        }

        // A read-only file makes File.Delete throw; the cleanup helper must swallow it.
        var file = _temp.CreateFile("readonly.tmp");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var act = () => HardLinkPreflight.TryDelete(file);
            act.Should().NotThrow();
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void TryDelete_is_a_noop_for_missing_paths()
    {
        var act = () => HardLinkPreflight.TryDelete(Path.Combine(_temp.Root, "nope"));

        act.Should().NotThrow();
    }
}
