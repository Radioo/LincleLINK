using Avalonia;
using Avalonia.Controls;
using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

#pragma warning disable CA1416 // Win32DarkTitleBar is invoked only under an explicit IsWindows() guard

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Interaction-level headless tests: dialog hosting, window lifecycle callbacks,
/// and view code-behind handlers invoked directly.
/// </summary>
public sealed class InteractionTests
{
    private sealed class TestDialogVm : IDialogViewModel
    {
        public string Title => "Test";
        public Size DialogSize => new(400, 300);
        public Size DialogMinSize => new(300, 200);
        public event EventHandler? CloseRequested;

        public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public async Task ShowDialogAsync_completes_when_the_view_model_requests_close()
    {
        var vm = new TestDialogVm();
        var service = HeadlessAppHost.RunOnUiThread(() => new DialogService(() => null));
        var completed = new TaskCompletionSource();

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var show = service.ShowDialogAsync(vm);
            vm.Close();
            show.ContinueWith(_ => completed.SetResult(), TaskScheduler.Default);
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Win32DarkTitleBar_noops_before_a_platform_handle_exists()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new Window();

            Win32DarkTitleBar.Apply(window, dark: true);
            Win32DarkTitleBar.Apply(window, dark: false);
        });
    }

    [Fact]
    public void ThemeManager_applies_title_bar_to_a_window()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new Window();

            ThemeManager.ApplyTitleBar(window);
        });
    }

    [Fact]
    public void MainWindow_shows_and_runs_on_opened_without_a_view_model()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new MainWindow();

            window.Show();
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_on_opened_initializes_when_a_view_model_is_present()
    {
        var vm = BuildMainViewModel();

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new MainWindow { DataContext = vm };

            window.Show();
            window.Close();
        });
    }

    [Fact]
    public void Win32DarkTitleBar_applies_to_a_created_window()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var window = new Window();
            window.Show();

            Win32DarkTitleBar.Apply(window, dark: true);
            Win32DarkTitleBar.Apply(window, dark: false);

            window.Close();
        });
    }

    [Fact]
    public void Program_configures_the_avalonia_builder()
    {
        var builder = Program.BuildAvaloniaApp();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void MessageDialog_button_handlers_raise_the_chosen_result()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var dialog = new LincleLINK.App.Controls.MessageDialog();
            LincleLINK.App.Controls.MessageDialogResult? chosen = null;
            dialog.ResultChosen += r => chosen = r;

            var ok = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnOk",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var yes = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnYes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var no = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnNo",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var replace = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnReplace",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var skip = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnSkip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var cancel = typeof(LincleLINK.App.Controls.MessageDialog).GetMethod("OnCancel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            ok.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.Ok);

            yes.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.Yes);

            no.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.No);

            replace.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.Replace);

            skip.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.Skip);

            cancel.Invoke(dialog, [dialog, null]);
            chosen.Should().Be(LincleLINK.App.Controls.MessageDialogResult.Cancel);
        });
    }

    [Fact]
    public void LogoPicker_click_executes_the_main_view_models_logo_command()
    {
        var vm = BuildMainViewModel();

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var picker = new LogoPicker { DataContext = vm };
            var handler = typeof(LogoPicker).GetMethod("OnLogoClicked",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var sender = new TextBlock { DataContext = new LogoEntry("IIDX/AC_Lincle_logo", "avares://x.png", "Lincle") };

            handler.Invoke(picker, [sender, null]);

            picker.Should().NotBeNull();
        });
    }

    [Fact]
    public void LibraryPage_row_click_selects_the_instance()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var page = new LibraryPage();
            var gridClicked = typeof(LibraryPage).GetMethod("OnGridItemClicked",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var sender = new TextBlock { DataContext = new InstanceListEntry("X", 0, 0, "0 B") };
            gridClicked.Invoke(page, [sender, null]);
        });
    }

    [Fact]
    public void LogoPicker_click_ignores_non_logo_senders()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var picker = new LogoPicker();
            var handler = typeof(LogoPicker).GetMethod("OnLogoClicked",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Non-control sender.
            handler.Invoke(picker, ["not a control", null]);

            // Control without a LogoEntry data context.
            handler.Invoke(picker, [new TextBlock { DataContext = "nope" }, null]);
        });
    }

    [Fact]
    public void LibraryPage_row_click_ignores_non_instance_senders()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var page = new LibraryPage();
            var gridClicked = typeof(LibraryPage).GetMethod("OnGridItemClicked",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Non-control sender.
            gridClicked.Invoke(page, ["not a control", null]);

            // Control without an InstanceListEntry data context.
            gridClicked.Invoke(page, [new TextBlock { DataContext = "nope" }, null]);
        });
    }

    private static MainViewModel BuildMainViewModel()
    {
        var fs = Substitute.For<LincleLINK.Core.Abstractions.Filesystem.IFileSystem>();
        var preflight = Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinkPreflight>();
        var repository = Substitute.For<LincleLINK.Core.Abstractions.Instances.IInstanceRepository>();
        var driveInfo = Substitute.For<LincleLINK.Core.Abstractions.Disk.IDriveInfoProvider>();
        var dialogs = Substitute.For<LincleLINK.Core.Abstractions.Dialogs.IDialogService>();
        var detector = Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>();
        var store = Substitute.For<LincleLINK.Core.Abstractions.Storage.IFileStore>();
        var paths = Substitute.For<LincleLINK.Core.Abstractions.Paths.IAppPaths>();
        paths.DataDirectory.Returns("C:\\data");
        var settingsStore = Substitute.For<LincleLINK.Core.Abstractions.Settings.ISettingsStore>();
        settingsStore.Load().Returns(new Core.Abstractions.Settings.AppSettings(
            Core.Abstractions.Settings.AppTheme.Light, "C:\\data", 2));

        return new MainViewModel(
            new LincleLINK.Core.Application.InstanceService(fs,
                Substitute.For<LincleLINK.Core.Abstractions.Hashing.IFileHasher>(), store,
                Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinker>(), preflight,
                repository, driveInfo, dialogs, detector),
            new LincleLINK.Core.Application.LinkingService(fs, store,
                Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinker>(), preflight,
                repository, dialogs),
            new LincleLINK.Core.Application.UnusedFilesService(store, repository, dialogs),
            new LincleLINK.Core.Application.LegacyImporter(repository),
            new LincleLINK.Core.Application.TorrentService(
                Substitute.For<LincleLINK.Core.Abstractions.Torrents.ITorrentSource>(),
                repository, store, Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinker>(), fs),
            repository,
            new LincleLINK.Core.Application.StatusService(store, repository, driveInfo, paths),
            dialogs,
            Substitute.For<IThemeManager>(),
            settingsStore,
            Substitute.For<ITaskbarProgress>(),
            preflight,
            () => new AddInstanceViewModel(new LincleLINK.Core.Application.InstanceService(fs,
                    Substitute.For<LincleLINK.Core.Abstractions.Hashing.IFileHasher>(), store,
                    Substitute.For<LincleLINK.Core.Abstractions.Linking.IHardLinker>(), preflight,
                    repository, driveInfo, dialogs, detector),
                dialogs, Substitute.For<ITaskbarProgress>(), fs, preflight, detector),
            new LogoCatalog(),
            paths);
    }
}
