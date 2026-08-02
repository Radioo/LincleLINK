using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Paths;

public sealed class AppPathsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Paths_derive_from_data_directory()
    {
        var root = Path.Combine(_temp.Root, "data");
        var paths = new AppPaths(root);

        paths.DataDirectory.Should().Be(root);
        paths.DbDirectory.Should().Be(Path.Combine(root, "db"));
        paths.InstanceDirectory.Should().Be(Path.Combine(root, "instance"));
    }

    [Fact]
    public void EnsureCreated_creates_db_but_not_instance()
    {
        var paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        paths.EnsureCreated();

        Directory.Exists(paths.DbDirectory).Should().BeTrue();
        // The instance folder is legacy-JSON-only and created lazily by the
        // migration path (plan 13); a fresh SQLite install has no need for it.
        Directory.Exists(paths.InstanceDirectory).Should().BeFalse();
    }
}
