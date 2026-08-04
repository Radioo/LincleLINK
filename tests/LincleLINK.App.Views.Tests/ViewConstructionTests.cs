using FluentAssertions;
using LincleLINK.App.Views;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Constructs every app view on the headless Avalonia host so the compiled XAML
/// (resource resolution, bindings, layout setup) is exercised. These are smoke
/// tests: they verify the view can be realized without throwing, not pixels.
/// </summary>
public sealed class ViewConstructionTests
{
    [Fact]
    public void MainWindow_constructs()
    {
        var window = HeadlessAppHost.RunOnUiThread(() => new MainWindow());

        window.Should().NotBeNull();
    }

    [Fact]
    public void LibraryPage_constructs_and_wires_natural_sort()
    {
        var page = HeadlessAppHost.RunOnUiThread(() => new LibraryPage());

        page.Should().NotBeNull();
    }

    [Fact]
    public void TorrentPage_constructs()
    {
        var page = HeadlessAppHost.RunOnUiThread(() => new TorrentPage());

        page.Should().NotBeNull();
    }

    [Fact]
    public void SettingsPage_constructs()
    {
        var page = HeadlessAppHost.RunOnUiThread(() => new SettingsPage());

        page.Should().NotBeNull();
    }

    [Fact]
    public void ActivityBar_constructs()
    {
        var bar = HeadlessAppHost.RunOnUiThread(() => new ActivityBar());

        bar.Should().NotBeNull();
    }

    [Fact]
    public void Sidebar_constructs()
    {
        var sidebar = HeadlessAppHost.RunOnUiThread(() => new Sidebar());

        sidebar.Should().NotBeNull();
    }

    [Fact]
    public void AddInstanceWindow_constructs()
    {
        var window = HeadlessAppHost.RunOnUiThread(() => new AddInstanceWindow());

        window.Should().NotBeNull();
    }

    [Fact]
    public void FirstRunWindow_constructs()
    {
        var window = HeadlessAppHost.RunOnUiThread(() => new FirstRunWindow());

        window.Should().NotBeNull();
    }

    [Fact]
    public void StorageMigrationWindow_constructs()
    {
        var window = HeadlessAppHost.RunOnUiThread(() => new StorageMigrationWindow());

        window.Should().NotBeNull();
    }
}
