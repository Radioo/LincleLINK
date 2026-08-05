using FluentAssertions;
using LincleLINK.App.Logos;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// <see cref="LogoCatalog"/> built-in catalog, key lookup, and custom-logo file
/// helpers (the VM tests exercise the in-memory side).
/// </summary>
public sealed class LogoCatalogTests : IDisposable
{
    private readonly string _root;

    public LogoCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LincleLINK.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Catalog_contains_every_builtin_logo_with_resources_paths()
    {
        var catalog = new LogoCatalog();

        catalog.AllLogos.Should().NotBeEmpty();
        catalog.AllLogos[0].LogoKey.Should().Be("IIDX/AC_9th_style_logo");
        catalog.AllLogos[0].AssetPath.Should().Be("avares://LincleLINK/Assets/IIDX/AC_9th_style_logo.png");
        catalog.GetLogoPath("IIDX/AC_9th_style_logo").Should().NotBeNull();
    }

    [Fact]
    public void GetLogoPath_handles_null_and_unknown_keys()
    {
        var catalog = new LogoCatalog();

        catalog.GetLogoPath(null).Should().BeNull();
        catalog.GetLogoPath("unknown/key").Should().BeNull();
    }

    [Fact]
    public void Custom_logo_path_resolves_only_when_the_file_exists()
    {
        LogoCatalog.GetCustomLogoFilePath(_root, "x").Should().BeNull();

        LogoCatalog.SaveCustomLogo(_root, "x", CreatePng());

        LogoCatalog.GetCustomLogoFilePath(_root, "x")
            .Should().Be(Path.Combine(_root, "custom_logos", "x.png"));
    }

    [Fact]
    public void DeleteCustomLogo_removes_the_file_and_tolerates_missing()
    {
        LogoCatalog.SaveCustomLogo(_root, "x", CreatePng());

        LogoCatalog.DeleteCustomLogo(_root, "x");
        LogoCatalog.GetCustomLogoFilePath(_root, "x").Should().BeNull();

        LogoCatalog.DeleteCustomLogo(_root, "missing");
    }

    private string CreatePng()
    {
        var path = Path.Combine(_root, "src.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
        return path;
    }
}
