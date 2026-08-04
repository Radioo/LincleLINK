using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

/// <summary>
/// Error and empty-state branches of <see cref="JsonInstanceRepository"/> that the
/// shared contract does not reach.
/// </summary>
public sealed class JsonInstanceRepositoryCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private (IAppPaths Paths, string InstanceDir) NewPaths()
    {
        var instanceDir = Path.Combine(_temp.Root, "data", "instance");
        var paths = Substitute.For<IAppPaths>();
        paths.InstanceDirectory.Returns(instanceDir);
        return (paths, instanceDir);
    }

    [Fact]
    public async Task GetNames_with_missing_instance_directory_returns_empty()
    {
        var (paths, _) = NewPaths();

        var names = await new JsonInstanceRepository(paths).GetNamesAsync();

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_failure_throws_InstanceStorageException()
    {
        var (paths, instanceDir) = NewPaths();
        var repo = new JsonInstanceRepository(paths);
        await repo.SaveAsync(Instance.Create("X", [], []));

        // Occupy the temp path with a directory so the atomic-write FileStream fails.
        Directory.CreateDirectory(Path.Combine(instanceDir, "X.json.tmp"));

        var act = () => repo.SaveAsync(Instance.Create("X", [new InstanceFile("f.bin", "", 1, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")], []));

        await act.Should().ThrowAsync<InstanceStorageException>();
    }

    [Fact]
    public async Task Delete_failure_on_windows_throws_InstanceStorageException()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("Read-only file deletion only throws on Windows.");
        }

        var (paths, instanceDir) = NewPaths();
        var repo = new JsonInstanceRepository(paths);
        await repo.SaveAsync(Instance.Create("X", [], []));
        var jsonPath = Path.Combine(instanceDir, "X.json");
        File.SetAttributes(jsonPath, FileAttributes.ReadOnly);

        try
        {
            var act = () => repo.DeleteAsync("X");
            await act.Should().ThrowAsync<InstanceStorageException>();
        }
        finally
        {
            File.SetAttributes(jsonPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task SetCustomLogo_is_a_noop_on_the_json_repository()
    {
        var (paths, _) = NewPaths();

        await new JsonInstanceRepository(paths).SetCustomLogoAsync("X", "custom");
    }

    [Fact]
    public async Task Null_json_content_throws_InstanceStorageException()
    {
        var (paths, instanceDir) = NewPaths();
        Directory.CreateDirectory(instanceDir);
        await File.WriteAllTextAsync(Path.Combine(instanceDir, "nil.json"), "null");
        var repo = new JsonInstanceRepository(paths);

        var act = () => repo.GetAsync("nil");

        await act.Should().ThrowAsync<InstanceStorageException>();
    }
}
