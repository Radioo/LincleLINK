using FluentAssertions;
using LincleLINK.App.ViewModels.FileTree;
using LincleLINK.Core.Domain;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class FileTreeViewModelTests
{
    /// <summary>
    /// Wall-clock allowance for one expand or collapse of 50k rows. The single range
    /// change asserted next to it is the real guard; the clock only catches a fall back
    /// to row-by-row changes, which costs far more than this. Wide on purpose: a CI
    /// runner with coverage instrumentation is several times slower than a desktop.
    /// </summary>
    private static readonly TimeSpan RangeChangeBudget = TimeSpan.FromSeconds(10);

    private static InstanceFile File(string directory, string name, long size = 1)
        => new(name, directory, size, $"{directory}/{name}.hash");

    private static IEnumerable<string> Names(FileTreeViewModel tree) => tree.Rows.Select(r => r.Name);

    [Fact]
    public void Load_shows_the_root_level_collapsed_with_folders_first()
    {
        var tree = new FileTreeViewModel();

        tree.Load(
            [File("", "readme.txt"), File("modules", "bm2dx.dll"), File(@"data\movie", "a.mp4"), File("", "Launcher.exe")],
            ["modules", "data", @"data\movie"]);

        Names(tree).Should().Equal("data", "modules", "Launcher.exe", "readme.txt");
        tree.Rows.Select(r => r.IsDirectory).Should().Equal(true, true, false, false);
        tree.Rows.Should().OnlyContain(r => r.Depth == 0 && !r.IsExpanded);
    }

    [Fact]
    public void Expand_inserts_the_folders_children_directly_under_it_one_level_deeper()
    {
        var tree = new FileTreeViewModel();
        tree.Load(
            [File("data", "b.bin"), File(@"data\movie", "a.mp4"), File("", "readme.txt")],
            []);

        tree.Expand(tree.Rows[0]);

        Names(tree).Should().Equal("data", "movie", "b.bin", "readme.txt");
        tree.Rows.Select(r => r.Depth).Should().Equal(0, 1, 1, 0);
        tree.Rows[0].IsExpanded.Should().BeTrue();
    }

    /// <summary>
    /// Plan 16 D11 risk check: a 150k file instance with one 50k file folder. The
    /// list must see one range change per expand or collapse, never one per row.
    /// The time bounds are loose on purpose, they catch a quadratic path, not jitter.
    /// </summary>
    [Fact]
    public void Expanding_a_huge_folder_raises_one_range_change_and_stays_fast()
    {
        var files = new List<InstanceFile>(150_000);
        for (var i = 0; i < 50_000; i++)
        {
            files.Add(File(@"data\sound", $"{i:D5}.2dx", 10));
        }

        for (var i = 0; i < 100_000; i++)
        {
            files.Add(File($@"data\graphic\{i / 100:D4}", $"{i:D6}.ifs", 10));
        }

        var tree = new FileTreeViewModel();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        tree.Load(files, []);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        tree.Rows.Should().ContainSingle().Which.FileCount.Should().Be(150_000);
        tree.Rows[0].Size.Should().Be(1_500_000);

        tree.Expand(tree.Rows[0]);
        var sound = tree.Rows.Single(r => r.Name == "sound");
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        tree.Rows.CollectionChanged += (_, e) => changes.Add(e);

        clock.Restart();
        tree.Expand(sound);
        clock.Elapsed.Should().BeLessThan(RangeChangeBudget);

        changes.Should().ContainSingle();
        changes[0].Action.Should().Be(System.Collections.Specialized.NotifyCollectionChangedAction.Add);
        changes[0].NewItems!.Count.Should().Be(50_000);
        tree.Rows.Should().HaveCount(50_003);

        changes.Clear();
        clock.Restart();
        tree.Collapse(sound);
        clock.Elapsed.Should().BeLessThan(RangeChangeBudget);

        changes.Should().ContainSingle();
        changes[0].Action.Should().Be(System.Collections.Specialized.NotifyCollectionChangedAction.Remove);
        tree.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void Empty_folders_show_and_paths_that_differ_only_by_case_share_one_folder()
    {
        var tree = new FileTreeViewModel();

        tree.Load(
            [File(@"data\graphic", "a.ifs", 3), File(@"Data\Graphic", "b.ifs", 4)],
            ["data", @"data\graphic", @"data\tmp"]);

        tree.Rows.Should().ContainSingle();
        var data = tree.Rows[0];
        data.Name.Should().Be("data");
        tree.Expand(data);
        Names(tree).Should().Equal("data", "graphic", "tmp");
        tree.Rows[1].FileCount.Should().Be(2);
        tree.Rows[1].Size.Should().Be(7);
        tree.Rows[2].FileCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Right_key_expands_a_folder_then_steps_into_it_and_left_does_the_reverse(bool viaFile)
    {
        var tree = new FileTreeViewModel();
        tree.Load([File("data", "a.bin")], []);
        var data = tree.Rows[0];

        tree.StepInto(data).Should().BeSameAs(data);
        data.IsExpanded.Should().BeTrue();
        var child = tree.StepInto(data);
        child.Name.Should().Be("a.bin");
        tree.StepInto(child).Should().BeSameAs(child);

        var from = viaFile ? child : data;
        if (viaFile)
        {
            tree.StepOut(from).Should().BeSameAs(data);
            data.IsExpanded.Should().BeTrue();
        }

        tree.StepOut(data).Should().BeSameAs(data);
        data.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Filter_shows_matches_with_their_folders_opened_and_clearing_it_restores_the_view()
    {
        var tree = new FileTreeViewModel();
        tree.Load(
            [
                File(@"data\movie", "intro.mp4"), File(@"data\movie", "outro.webm"),
                File("modules", "bm2dx.dll"), File("", "readme.txt"),
            ],
            []);
        tree.Expand(tree.Rows[1]);
        Names(tree).Should().Equal("data", "modules", "bm2dx.dll", "readme.txt");

        tree.Filter = "MP4";

        Names(tree).Should().Equal("data", "movie", "intro.mp4");

        tree.Filter = "";

        Names(tree).Should().Equal("data", "modules", "bm2dx.dll", "readme.txt");
    }

    [Fact]
    public void Filter_matching_a_folder_name_keeps_its_contents_reachable()
    {
        var tree = new FileTreeViewModel();
        tree.Load([File(@"data\movie", "intro.mp4"), File("data", "x.bin")], []);

        tree.Filter = "movie";

        Names(tree).Should().Equal("data", "movie");
        tree.Expand(tree.Rows[1]);
        Names(tree).Should().Equal("data", "movie", "intro.mp4");
    }

    [Fact]
    public void Collapse_hides_everything_below_the_folder_and_expanding_again_restores_nested_state()
    {
        var tree = new FileTreeViewModel();
        tree.Load(
            [File("data", "b.bin"), File(@"data\movie", "a.mp4"), File("", "readme.txt")],
            []);
        tree.Expand(tree.Rows[0]);
        tree.Expand(tree.Rows[1]);
        Names(tree).Should().Equal("data", "movie", "a.mp4", "b.bin", "readme.txt");

        tree.Collapse(tree.Rows[0]);

        Names(tree).Should().Equal("data", "readme.txt");
        tree.Rows[0].IsExpanded.Should().BeFalse();

        tree.Expand(tree.Rows[0]);

        Names(tree).Should().Equal("data", "movie", "a.mp4", "b.bin", "readme.txt");
    }
}
