using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Layout regression tests for progress indicators, with the real Semi theme
/// loaded. The themed <see cref="ProgressBar"/> has a default MinWidth that is wider
/// than most places a bar goes; a bar given less room spills out of it and the text
/// next to it is drawn on top of the bar. View model tests can't see that, so these
/// lay the views out and check the rectangles: no visible text may overlap a bar,
/// and no bar may be wider than its parent.
/// </summary>
public sealed class ProgressLayoutTests
{
    private static readonly DateTime Stamp = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Runs the dispatcher until <paramref name="done"/>, the way the app's UI thread keeps pumping.</summary>
    private static void PumpUntil(Func<bool> done)
    {
        var waited = Stopwatch.StartNew();
        while (!done() && waited.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        done().Should().BeTrue("the view model should get there while the UI thread keeps pumping");
    }

    /// <summary>The part of <paramref name="visual"/> that is actually drawn: its bounds cut by every clipping ancestor.</summary>
    private static Rect RectIn(Visual root, Visual visual)
    {
        var rect = new Rect(visual.TranslatePoint(new Point(0, 0), root) ?? default, visual.Bounds.Size);
        for (var ancestor = visual.GetVisualParent(); ancestor is not null && ancestor != root; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor.ClipToBounds)
            {
                var clip = new Rect(ancestor.TranslatePoint(new Point(0, 0), root) ?? default, ancestor.Bounds.Size);
                rect = rect.Intersect(clip);
            }
        }

        return rect;
    }

    /// <summary>Lays <paramref name="view"/> out at the given size and checks every visible bar in it.</summary>
    private static void AssertBarsAreClear(Control view, double width, double height, int expectedBars)
    {
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var bars = view.GetVisualDescendants().OfType<ProgressBar>()
                .Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0).ToList();
            bars.Should().HaveCount(expectedBars, "the state under test should show exactly these indicators");

            var texts = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Bounds.Width > 0)
                .Where(t => RectIn(view, t) is { Width: > 0.5, Height: > 0.5 })
                .ToList();

            // The file tree is what the files dialog is for. Whatever else shows, it
            // keeps room for a handful of rows, down to the smallest window.
            foreach (var tree in view.GetVisualDescendants().OfType<FileTreeView>())
            {
                RectIn(view, tree).Height.Should().BeGreaterThan(
                    120, "the drop zone, the source list and the footer must not squeeze the tree out");
            }

            // In a view with a file tree, the tree is the only thing that scrolls. A
            // scroll container around anything else means that thing was given less
            // room than it needs, and cuts it off.
            if (view.GetVisualDescendants().OfType<FileTreeView>().FirstOrDefault() is { } fileTree)
            {
                foreach (var scroller in view.GetVisualDescendants().OfType<ScrollViewer>()
                             .Where(s => s.IsEffectivelyVisible && !fileTree.IsVisualAncestorOf(s)))
                {
                    scroller.Extent.Height.Should().BeLessThanOrEqualTo(
                        scroller.Viewport.Height + 0.5,
                        $"only the file tree may scroll, but a {scroller.Parent?.GetType().Name} holds {scroller.Extent.Height} px in a {scroller.Viewport.Height} px viewport");
                }

                // Nor may anything outside the tree be cut off by a clipping parent.
                // (A text box clips its own placeholder and content; that is its job.)
                foreach (var text in view.GetVisualDescendants().OfType<TextBlock>()
                             .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && !fileTree.IsVisualAncestorOf(t))
                             .Where(t => t.FindAncestorOfType<TextBox>() is null))
                {
                    var drawn = RectIn(view, text);
                    (drawn.Height >= text.Bounds.Height - 0.5 && drawn.Width >= text.Bounds.Width - 0.5).Should().BeTrue(
                        $"the text '{text.Text}' is {text.Bounds.Size} but only {drawn.Size} of it is drawn");
                }
            }

            // No two pieces of text on top of each other either: that is how a row
            // with more in it than fits shows up.
            for (var i = 0; i < texts.Count; i++)
            {
                for (var j = i + 1; j < texts.Count; j++)
                {
                    var overlap = RectIn(view, texts[i]).Intersect(RectIn(view, texts[j]));
                    (overlap.Width > 0.5 && overlap.Height > 0.5).Should().BeFalse(
                        $"the texts '{texts[i].Text}' at {RectIn(view, texts[i])} and '{texts[j].Text}' at {RectIn(view, texts[j])} must not overlap");
                }
            }

            foreach (var bar in bars)
            {
                var barRect = RectIn(view, bar);

                // The bar's own template may hold text (a percentage); that is part of the bar.
                foreach (var text in texts.Where(t => !bar.IsVisualAncestorOf(t)))
                {
                    var overlap = barRect.Intersect(RectIn(view, text));
                    (overlap.Width > 0.5 && overlap.Height > 0.5).Should().BeFalse(
                        $"the text '{text.Text}' at {RectIn(view, text)} must not be drawn over the progress bar at {barRect}");
                }

                if (bar.GetVisualParent() is Visual parent)
                {
                    bar.Bounds.Width.Should().BeLessThanOrEqualTo(
                        parent.Bounds.Width + 0.5, "a bar wider than its parent has spilled out of the room it was given");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static InstanceFilesViewModel CreateFilesViewModel(IFileSystem fs, IFileHasher hasher)
    {
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetAsync("A", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "A", [new InstanceFile("bm2dx.dll", "modules", 2048, "AA.dll"), new InstanceFile("readme.txt", "", 1, "BB.txt")], []));
        var store = Substitute.For<IFileStore>();
        var dialogs = Substitute.For<IDialogService>();
        var updates = new InstanceUpdateService(
            fs, hasher, store, repository, Substitute.For<IDriveInfoProvider>(), Substitute.For<IAppPaths>(), dialogs,
            Substitute.For<IGameVersionDetector>(), NullLogger<InstanceUpdateService>.Instance);
        return new InstanceFilesViewModel(
            repository, store, dialogs, updates, Substitute.For<ITaskbarProgress>(), NullLogger<InstanceFilesViewModel>.Instance);
    }

    [Theory]
    [InlineData(1100, 640)]
    [InlineData(760, 480)] // the smallest the main window allows, minus the overlay's margins
    public void The_files_dialog_activity_line_sits_next_to_its_bar(double width, double height)
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = CreateFilesViewModel(Substitute.For<IFileSystem>(), Substitute.For<IFileHasher>());
            var loading = vm.LoadAsync("A");
            PumpUntil(() => loading.IsCompleted && !vm.IsPlanning && !vm.IsDetecting);

            vm.IsLoading = true; // "Loading the entry's files..." with its indeterminate bar
            var view = new InstanceFilesView { DataContext = vm };

            AssertBarsAreClear(view, width, height, expectedBars: 1);
        });
    }

    [Theory]
    [InlineData(1100, 640)]
    [InlineData(760, 480)]
    public void A_hashing_source_row_and_a_running_apply_keep_their_text_off_their_bars(double width, double height)
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var fs = Substitute.For<IFileSystem>();
            fs.DirectoryExists("/drop/pack").Returns(true);
            fs.ListDirectory("/drop/pack").Returns([new FileSystemEntry("/drop/pack/new.bin", "new.bin", false, 7, Stamp, null)]);
            var hashing = new TaskCompletionSource<string>();
            var hasher = Substitute.For<IFileHasher>();
            hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(hashing.Task);
            var vm = CreateFilesViewModel(fs, hasher);
            var loading = vm.LoadAsync("A");
            PumpUntil(() => loading.IsCompleted);

            var adding = vm.AddPathsAsync(["/drop/pack"]);
            PumpUntil(() => vm.Sources.Count == 1 && !vm.IsPlanning && !vm.IsDetecting);
            vm.Sources[0].IsHashing.Should().BeTrue();

            // Apply's footer: bar, status line and Cancel.
            vm.Progress = 40;
            vm.StatusLine = "Added /drop/pack/new.bin to storage";
            vm.IsBusy = true;
            var view = new InstanceFilesView { DataContext = vm };

            try
            {
                AssertBarsAreClear(view, width, height, expectedBars: 2);
            }
            finally
            {
                hashing.SetResult("AAAA");
                PumpUntil(() => adding.IsCompleted);
            }
        });
    }

    [Theory]
    [InlineData(1040, 600, 1)] // the state of the bug report: one drop of several items
    [InlineData(1040, 600, 4)]
    [InlineData(760, 480, 2)]
    public void Staged_sources_never_scroll_are_never_cut_off_and_leave_the_tree_its_room(
        double width, double height, int sources)
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var fs = Substitute.For<IFileSystem>();
            var hasher = Substitute.For<IFileHasher>();
            hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("AAAA");
            for (var i = 0; i < sources; i++)
            {
                fs.FileExists($"/drop/{i}/a.bin").Returns(true);
                fs.FileExists($"/drop/{i}/b.bin").Returns(true);
                fs.GetFileLength(Arg.Any<string>()).Returns(1000);
            }

            var vm = CreateFilesViewModel(fs, hasher);
            var loading = vm.LoadAsync("A");
            PumpUntil(() => loading.IsCompleted);
            for (var i = 0; i < sources; i++)
            {
                var adding = vm.AddPathsAsync([$"/drop/{i}/a.bin", $"/drop/{i}/b.bin"]);
                PumpUntil(() => adding.IsCompleted);
            }

            PumpUntil(() => !vm.IsPlanning && !vm.IsDetecting);
            vm.Sources.Should().HaveCount(sources);

            // No bars: nothing is hashing, loading or applying. What is checked here is
            // that only the tree scrolls, nothing is clipped, nothing overlaps, and
            // the tree keeps its height.
            AssertBarsAreClear(new InstanceFilesView { DataContext = vm }, width, height, expectedBars: 0);
        });
    }

    [Theory]
    [InlineData(900, 600)]
    [InlineData(600, 480)] // the page area at the main window's minimum size
    public void The_library_page_loading_state_is_laid_out_cleanly(double width, double height)
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            // A new shell is loading its library until the first load finishes.
            var vm = InteractionTests.BuildMainViewModel();
            vm.IsLibraryLoading.Should().BeTrue();

            AssertBarsAreClear(new LibraryPage { DataContext = vm }, width, height, expectedBars: 1);
        });
    }

    [Fact]
    public void The_sidebar_keeps_its_measuring_headline_off_its_bar()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel();
            vm.IsStatusLoading.Should().BeTrue();

            // 200 wide, as the shell hosts it; the headline wraps there.
            AssertBarsAreClear(new Sidebar { DataContext = vm }, 200, 640, expectedBars: 1);
        });
    }

    [Fact]
    public void The_duplicate_dialog_keeps_its_status_under_its_bar()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var service = new InstanceService(
                Substitute.For<IFileSystem>(), Substitute.For<IFileHasher>(), Substitute.For<IFileStore>(),
                Substitute.For<IHardLinker>(), Substitute.For<IHardLinkPreflight>(), Substitute.For<IInstanceRepository>(),
                Substitute.For<IDriveInfoProvider>(), Substitute.For<IDialogService>(), Substitute.For<IGameVersionDetector>(),
                NullLogger<InstanceService>.Instance);
            var vm = new DuplicateInstanceViewModel(service, NullLogger<DuplicateInstanceViewModel>.Instance);
            vm.Start("beatmania IIDX 32 Pinky Crush with a rather long name");
            vm.StatusLine = "Saving beatmania IIDX 32 Pinky Crush with a rather long name - copy (148213 files)...";
            vm.IsBusy = true;

            // 420 wide, as the shell hosts it.
            AssertBarsAreClear(new DuplicateInstanceView { DataContext = vm }, 420, 320, expectedBars: 1);
        });
    }

    [Theory]
    [InlineData(1120)]
    [InlineData(840)] // the main window's minimum width
    public void The_shell_activity_bar_keeps_its_status_next_to_its_bar(double width)
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var vm = InteractionTests.BuildMainViewModel();
            vm.OperationName = "Deploy to folder";
            vm.OperationStatus = "Deploying beatmania IIDX 32 Pinky Crush with a rather long name to a rather long folder...";
            vm.IsBusy = true;

            // Indeterminate first (no percentage shown), then counting.
            AssertBarsAreClear(new ActivityBar { DataContext = vm }, width, 60, expectedBars: 1);

            vm.Progress = 42;
            AssertBarsAreClear(new ActivityBar { DataContext = vm }, width, 60, expectedBars: 1);
        });
    }
}
