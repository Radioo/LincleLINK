using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.ViewModels.FileTree;
using LincleLINK.App.Views;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Plan 16 D11 risk check against the real <see cref="ListBox"/>: a 50k file folder
/// must expand without realizing a container per row.
/// </summary>
public sealed class FileTreeViewTests
{
    [Fact]
    public void Expanding_a_50k_file_folder_stays_virtualized_and_fast()
    {
        var files = new List<InstanceFile>(150_000);
        for (var i = 0; i < 50_000; i++)
        {
            files.Add(new InstanceFile($"{i:D5}.2dx", @"data\sound", 10, $"s{i}"));
        }

        for (var i = 0; i < 100_000; i++)
        {
            files.Add(new InstanceFile($"{i:D6}.ifs", $@"data\graphic\{i / 100:D4}", 10, $"g{i}"));
        }

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var tree = new FileTreeViewModel();
            tree.Load(files, []);
            var view = new FileTreeView { DataContext = tree };
            var window = new Window { Content = view, Width = 700, Height = 500 };
            window.Show();

            tree.Expand(tree.Rows[0]);
            var sound = tree.Rows.Single(r => r.Name == "sound");

            var clock = Stopwatch.StartNew();
            tree.Expand(sound);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            clock.Stop();

            var realized = view.GetVisualDescendants().OfType<ListBoxItem>().Count();
            window.Close();

            tree.Rows.Should().HaveCount(50_003);
            realized.Should().BeInRange(5, 100, "only the rows on screen should get a container");

            // The guard against a non-virtualized list is the realized count above. The
            // clock only backs it up against a path that builds a container per row,
            // which takes minutes, so the bound is wide: a CI runner with coverage
            // instrumentation needed 1.8 s for what takes 0.3 s on a desktop.
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
        });
    }

    /// <summary>Runs the dispatcher until <paramref name="task"/> is done, the way the app's UI thread keeps pumping.</summary>
    private static void Pump(Task task)
    {
        var waited = Stopwatch.StartNew();
        while (!task.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(20))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        task.IsCompleted.Should().BeTrue("the view model work should finish while the UI thread keeps pumping");
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void The_files_dialog_resolves_through_the_view_locator_and_lists_the_entrys_rows()
    {
        var repository = Substitute.For<LincleLINK.Core.Abstractions.Instances.IInstanceRepository>();
        repository.GetAsync("A", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "A", [new InstanceFile("bm2dx.dll", "modules", 2048, "AA.dll"), new InstanceFile("readme.txt", "", 1, "BB.txt")], []));

        HeadlessAppHost.RunOnUiThread(() =>
        {
            var store = Substitute.For<LincleLINK.Core.Abstractions.Storage.IFileStore>();
            var dialogs = Substitute.For<LincleLINK.Core.Abstractions.Dialogs.IDialogService>();
            var vm = new InstanceFilesViewModel(
                repository,
                store,
                dialogs,
                new LincleLINK.Core.Application.InstanceUpdateService(
                    Substitute.For<LincleLINK.Core.Abstractions.Filesystem.IFileSystem>(),
                    Substitute.For<LincleLINK.Core.Abstractions.Hashing.IFileHasher>(),
                    store,
                    repository,
                    Substitute.For<LincleLINK.Core.Abstractions.Disk.IDriveInfoProvider>(),
                    Substitute.For<LincleLINK.Core.Abstractions.Paths.IAppPaths>(),
                    dialogs,
                    Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>(),
                    NullLogger<LincleLINK.Core.Application.InstanceUpdateService>.Instance),
                Substitute.For<LincleLINK.App.Abstractions.ITaskbarProgress>(),
                NullLogger<InstanceFilesViewModel>.Instance);
            // Never block this thread on the view model: its work comes back through
            // this thread's dispatcher, exactly as in the app.
            Pump(vm.LoadAsync("A"));

            var view = new ViewLocator().Build(vm);
            view.Should().BeOfType<InstanceFilesView>();
            view!.DataContext = vm;
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();
            window.UpdateLayout();

            var names = view.GetVisualDescendants().OfType<ListBoxItem>()
                .Select(i => ((FileTreeRow)i.DataContext!).Name).ToList();

            // Stage a removal in update mode: the row's badge and the footer's
            // Apply button have to show up through the real bindings.
            vm.IsUpdating = true;
            Pump(vm.StageRemovalAsync([vm.Tree.Rows.Single(r => r.Name == "readme.txt")]));

            // Apply stays off until the version detection for the new plan has settled.
            Pump(Task.Run(async () =>
            {
                while (vm.IsDetecting)
                {
                    await Task.Delay(5);
                }
            }));

            window.UpdateLayout();
            var visibleTexts = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            var applyShows = view.GetVisualDescendants().OfType<Button>()
                .Any(b => b.IsEffectivelyVisible && Equals(b.Content, "Apply") && b.IsEffectivelyEnabled);
            window.Close();

            names.Should().Equal("modules", "readme.txt");
            visibleTexts.Should().Contain("removed").And.Contain("Drop files or folders here");
            visibleTexts.Should().Contain(t => t != null && t.Contains("1 file, 1 B will be removed from this entry"));
            applyShows.Should().BeTrue();
        });
    }
}
