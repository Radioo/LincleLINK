using System.Globalization;
using Avalonia.Controls;
using Avalonia.Styling;
using FluentAssertions;
using LincleLINK.App;
using LincleLINK.App.Behaviors;
using LincleLINK.App.Controls;
using LincleLINK.App.Converters;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Headless coverage for the app's UI services and controls that need an Avalonia
/// runtime (resource loading, controls, theme application). All Avalonia access is
/// marshaled to the headless UI thread.
/// </summary>
public sealed class UIServicesTests
{
    [Fact]
    public void ViewLocator_matches_view_models_only()
    {
        var locator = HeadlessAppHost.RunOnUiThread(() => new ViewLocator());
        var firstRunVm = HeadlessAppHost.RunOnUiThread(() =>
            new FirstRunViewModel(
                NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Dialogs.IDialogService>(),
                NSubstitute.Substitute.For<LincleLINK.App.Abstractions.IThemeManager>(),
                @"C:\data", false, AppTheme.Light));

        locator.Match(firstRunVm).Should().BeTrue();
        locator.Match("not a vm").Should().BeFalse();
        locator.Build(null).Should().BeNull();
        locator.Build("unknown").Should().BeOfType<TextBlock>();
    }

    [Fact]
    public void ViewLocator_resolves_AddInstanceViewModel_to_its_window()
    {
        var locator = HeadlessAppHost.RunOnUiThread(() => new ViewLocator());

        var view = HeadlessAppHost.RunOnUiThread(() =>
        {
            var fs = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Filesystem.IFileSystem>();
            var preflight = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinkPreflight>();
            var repository = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Instances.IInstanceRepository>();
            var driveInfo = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Disk.IDriveInfoProvider>();
            var dialogs = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Dialogs.IDialogService>();
            var detector = NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>();
            var service = new LincleLINK.Core.Application.InstanceService(
                fs,
                NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Hashing.IFileHasher>(),
                NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Storage.IFileStore>(),
                NSubstitute.Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinker>(),
                preflight, repository, driveInfo, dialogs, detector, NullLogger<LincleLINK.Core.Application.InstanceService>.Instance);

            return locator.Build(new AddInstanceViewModel(
                service, dialogs,
                NSubstitute.Substitute.For<LincleLINK.App.Abstractions.ITaskbarProgress>(),
                fs, preflight, NullLogger<AddInstanceViewModel>.Instance, detector));
        });

        view.Should().BeOfType<LincleLINK.App.Views.AddInstanceWindow>();
    }

    [Fact]
    public void LogoSourceConverter_converts_resources_and_files()
    {
        var converter = HeadlessAppHost.RunOnUiThread(() => new LogoSourceConverter());

        HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture)).Should().BeNull();
        HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(string.Empty, typeof(object), null, CultureInfo.InvariantCulture)).Should().BeNull();

        var bitmap = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert("avares://LincleLINK/Assets/IIDX/AC_9th_style_logo.png", typeof(object), null, CultureInfo.InvariantCulture));
        bitmap.Should().NotBeNull();

        var again = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert("avares://LincleLINK/Assets/IIDX/AC_9th_style_logo.png", typeof(object), null, CultureInfo.InvariantCulture));
        again.Should().BeSameAs(bitmap);

        var missing = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert("Z:\\does-not-exist.png", typeof(object), null, CultureInfo.InvariantCulture));
        missing.Should().BeNull();

        // A real image file converts too.
        var dir = Path.Combine(Path.GetTempPath(), "LincleLINK.App.Views.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "logo.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var fromFile = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(png, typeof(object), null, CultureInfo.InvariantCulture));
        fromFile.Should().NotBeNull();

        var act = () => converter.ConvertBack(new object(), typeof(object), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void LogoSourceConverter_evicts_file_cache_for_overwritten_custom_logos()
    {
        // Regression (High 4): custom logos always overwrite the same path, so
        // without eviction a cache hit served the previous image forever.
        var converter = HeadlessAppHost.RunOnUiThread(() => new LogoSourceConverter());
        var dir = Path.Combine(Path.GetTempPath(), "LincleLINK.App.Views.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "logo.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01]);

        var first = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(png, typeof(object), null, CultureInfo.InvariantCulture));
        first.Should().NotBeNull();

        // Same path, same cached instance - the stale-bitmap case.
        var cached = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(png, typeof(object), null, CultureInfo.InvariantCulture));
        cached.Should().BeSameAs(first);

        // Eviction forces a fresh read on the next conversion.
        HeadlessAppHost.RunOnUiThread(() => LogoSourceConverter.Evict(png));
        var fresh = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(png, typeof(object), null, CultureInfo.InvariantCulture));
        fresh.Should().NotBeSameAs(first);
    }

    [Fact]
    public void SaveCustomLogo_evicts_the_cached_bitmap_for_its_path()
    {
        // The write path must evict the converter cache, so replacing image A with
        // image B on the same path shows B immediately (not after restart).
        var converter = HeadlessAppHost.RunOnUiThread(() => new LogoSourceConverter());
        var dir = Path.Combine(Path.GetTempPath(), "LincleLINK.App.Views.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var srcA = Path.Combine(dir, "a.png");
        var srcB = Path.Combine(dir, "b.png");
        File.WriteAllBytes(srcA, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01]);
        File.WriteAllBytes(srcB, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x02]);

        LogoCatalog.SaveCustomLogo(dir, "game", srcA);
        var path = LogoCatalog.GetCustomLogoFilePath(dir, "game")!;
        var first = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(path, typeof(object), null, CultureInfo.InvariantCulture));
        first.Should().NotBeNull();

        // Overwrite the same custom-logo path with a different source image.
        LogoCatalog.SaveCustomLogo(dir, "game", srcB);

        var after = HeadlessAppHost.RunOnUiThread(() =>
            converter.Convert(path, typeof(object), null, CultureInfo.InvariantCulture));
        after.Should().NotBeSameAs(first);
    }

    [Fact]
    public void ThemeManager_applies_dark_light_and_system()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var manager = new ThemeManager();

            manager.Apply(AppTheme.Dark);
            Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);

            manager.Apply(AppTheme.Light);
            Avalonia.Application.Current.RequestedThemeVariant.Should().Be(ThemeVariant.Light);

            manager.Apply(AppTheme.System);
            Avalonia.Application.Current.RequestedThemeVariant.Should().Be(ThemeVariant.Default);
        });
    }

    [Fact]
    public void MessageDialog_configures_button_sets()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var ok = new MessageDialog();
            ok.Configure("hi", MessageDialogButtons.Ok);
            ok.OkButton.IsVisible.Should().BeTrue();
            ok.YesButton.IsVisible.Should().BeFalse();

            var yesNo = new MessageDialog();
            yesNo.Configure("hi", MessageDialogButtons.YesNo);
            yesNo.YesButton.IsVisible.Should().BeTrue();
            yesNo.NoButton.IsVisible.Should().BeTrue();

            var replaceSkipCancel = new MessageDialog();
            replaceSkipCancel.Configure("hi", MessageDialogButtons.ReplaceSkipCancel);
            replaceSkipCancel.ReplaceButton.IsVisible.Should().BeTrue();
            replaceSkipCancel.SkipButton.IsVisible.Should().BeTrue();
            replaceSkipCancel.CancelButton.IsVisible.Should().BeTrue();
        });
    }

    [Fact]
    public void AutoScrollBehavior_set_get_and_reset()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var listBox = new ListBox();

            AutoScrollBehavior.GetAutoScrollToEnd(listBox).Should().BeFalse();

            AutoScrollBehavior.SetAutoScrollToEnd(listBox, true);
            AutoScrollBehavior.GetAutoScrollToEnd(listBox).Should().BeTrue();

            AutoScrollBehavior.SetAutoScrollToEnd(listBox, false);
            AutoScrollBehavior.GetAutoScrollToEnd(listBox).Should().BeFalse();
        });
    }

    [Fact]
    public async Task DialogService_without_an_owner_window_throws_on_pickers()
    {
        var service = HeadlessAppHost.RunOnUiThread(() => new DialogService(() => null));

        var folderAct = () => service.PickFolderAsync("title");
        await folderAct.Should().ThrowAsync<InvalidOperationException>();

        var fileAct = () => service.PickOpenFileAsync("title", new Core.Abstractions.Dialogs.FileType("Torrent files", ["*.torrent"]));
        await fileAct.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void ProgressBridge_batches_through_the_ui_dispatcher_when_an_app_exists()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var received = new List<string>();
            var progress = ProgressBridge.Create<string>(received.Add, batchSize: 2);

            progress.Report("a");
            progress.Report("b");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            received.Should().Contain("a");
            received.Should().Contain("b");
        });
    }
}
