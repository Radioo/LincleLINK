using FluentAssertions;
using LincleLINK.App.ViewModels.FileTree;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>The tree as the preview of an Update plan (plan 16 D6, D7).</summary>
public sealed class FileTreePlanTests
{
    private static readonly Instance Existing = Instance.Create(
        "IIDX 32",
        [
            new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"),
            new InstanceFile("avs2.dll", "modules", 50, "AVS.dll"),
            new InstanceFile("a.mp4", "data/movie", 5, "A.mp4"),
            new InstanceFile("readme.txt", "", 1, "R.txt"),
        ],
        ["modules", "data", "data/movie", "data/tmp"]);

    private static UpdateSource Pack => new(
        "",
        [new SourceFile("modules", "bm2dx.dll", 120, "NEW.dll"), new SourceFile("data/sound", "x.2dx", 7, "X.2dx")],
        []);

    private static FileTreeRow Row(FileTreeViewModel tree, string name) => tree.Rows.Single(r => r.Name == name);

    [Fact]
    public void Rows_carry_their_status_and_folders_count_the_changes_below_them()
    {
        var tree = new FileTreeViewModel();

        tree.Load(UpdatePlanner.Compute(Existing, [Pack], removedPaths: ["data/movie"]));
        tree.ExpandAll();

        Row(tree, "bm2dx.dll").Status.Should().Be(PlannedStatus.Replaced);
        Row(tree, "bm2dx.dll").Detail.Should().Be("100 B -> 120 B");
        Row(tree, "x.2dx").Status.Should().Be(PlannedStatus.Added);
        Row(tree, "avs2.dll").Status.Should().Be(PlannedStatus.Unchanged);
        Row(tree, "avs2.dll").IsDimmed.Should().BeTrue();
        Row(tree, "a.mp4").IsRemoved.Should().BeTrue();
        Row(tree, "movie").IsRemoved.Should().BeTrue();
        Row(tree, "tmp").IsRemoved.Should().BeFalse();
        Row(tree, "data").ChangeCount.Should().Be(2);
        Row(tree, "modules").ChangeCount.Should().Be(1);
        Row(tree, "data").Path.Should().Be("data");
        Row(tree, "x.2dx").Path.Should().Be("data/sound/x.2dx");
        tree.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_dimmed_while_there_are_no_changes()
    {
        var tree = new FileTreeViewModel();

        tree.Load(UpdatePlanner.Compute(Existing, []));

        tree.HasChanges.Should().BeFalse();
        tree.Rows.Should().OnlyContain(r => !r.IsDimmed);
    }

    [Fact]
    public void Reloading_keeps_the_open_folders_open()
    {
        var tree = new FileTreeViewModel();
        tree.Load(UpdatePlanner.Compute(Existing, []));
        tree.Expand(Row(tree, "data"));
        tree.Expand(Row(tree, "movie"));

        tree.Load(UpdatePlanner.Compute(Existing, [], removedPaths: ["data/movie/a.mp4"]));

        tree.Rows.Select(r => r.Name).Should().Equal("data", "movie", "a.mp4", "tmp", "modules", "readme.txt");
    }

    [Fact]
    public void ExpandChanges_opens_only_the_paths_that_lead_to_a_change()
    {
        var tree = new FileTreeViewModel();
        tree.Load(UpdatePlanner.Compute(Existing, [Pack]));

        tree.ExpandChanges();

        tree.Rows.Select(r => r.Name).Should().Equal(
            "data", "movie", "sound", "x.2dx", "tmp", "modules", "avs2.dll", "bm2dx.dll", "readme.txt");
    }

    [Fact]
    public void ChangesOnly_hides_everything_that_stays_as_it_is()
    {
        var tree = new FileTreeViewModel();
        tree.Load(UpdatePlanner.Compute(Existing, [Pack]));

        tree.ChangesOnly = true;

        tree.Rows.Select(r => r.Name).Should().Equal("data", "sound", "x.2dx", "modules", "bm2dx.dll");

        tree.ChangesOnly = false;

        tree.Rows.Select(r => r.Name).Should().Equal("data", "modules", "readme.txt");
    }

    [Fact]
    public void An_unticked_source_file_shows_as_excluded_and_an_unticked_replacement_marks_the_kept_file()
    {
        var tree = new FileTreeViewModel();

        tree.Load(UpdatePlanner.Compute(Existing, [Pack], excludedPaths: ["data/sound", "modules/bm2dx.dll"]));
        tree.ExpandAll();

        Row(tree, "x.2dx").IsExcluded.Should().BeTrue();
        Row(tree, "sound").IsExcluded.Should().BeTrue();
        Row(tree, "bm2dx.dll").Status.Should().Be(PlannedStatus.Unchanged);
        Row(tree, "bm2dx.dll").IsExcluded.Should().BeTrue();
        Row(tree, "data").ChangeCount.Should().Be(0);
    }
}
