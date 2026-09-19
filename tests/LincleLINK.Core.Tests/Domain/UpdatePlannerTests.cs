using FluentAssertions;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using Xunit;

namespace LincleLINK.Core.Tests.Domain;

/// <summary>
/// <see cref="UpdatePlanner"/> (plan 16 D12): what an Instance looks like after its
/// Pending changes apply. Paths are compared canonicalized, since stored separators
/// follow the host platform.
/// </summary>
public sealed class UpdatePlannerTests
{
    private static Instance Existing(params InstanceFile[] files)
        => Instance.Create("IIDX 32", files, files.Select(f => f.RelativePath).Distinct());

    private static UpdateSource Source(string destination, params SourceFile[] files)
        => new(destination, files, []);

    private static string PathOf(PlannedFile planned)
        => PathNormalizer.Canonicalize($"{planned.File.RelativePath}/{planned.File.FileName}");

    private static Dictionary<string, PlannedStatus> Statuses(UpdatePlan plan)
        => plan.Files.ToDictionary(PathOf, f => f.Status);

    [Fact]
    public void Source_files_are_added_replaced_or_identical_and_the_rest_stays_unchanged()
    {
        var instance = Existing(
            new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"),
            new InstanceFile("avs2.dll", "modules", 50, "SAME.dll"),
            new InstanceFile("readme.txt", "", 5, "README.txt"));

        var plan = UpdatePlanner.Compute(instance, [Source(
            "",
            new SourceFile("modules", "bm2dx.dll", 120, "NEW.dll"),
            new SourceFile("modules", "avs2.dll", 50, "SAME.dll"),
            new SourceFile("data/movie", "intro.mp4", 900, "MOVIE.mp4"))]);

        Statuses(plan).Should().BeEquivalentTo(new Dictionary<string, PlannedStatus>
        {
            ["modules/bm2dx.dll"] = PlannedStatus.Replaced,
            ["modules/avs2.dll"] = PlannedStatus.Identical,
            ["data/movie/intro.mp4"] = PlannedStatus.Added,
            ["readme.txt"] = PlannedStatus.Unchanged,
        });
        var replaced = plan.Files.Single(f => f.Status == PlannedStatus.Replaced);
        replaced.File.HashedFileName.Should().Be("NEW.dll");
        replaced.Previous!.HashedFileName.Should().Be("OLD.dll");
        plan.Directories.Select(PathNormalizer.Canonicalize).Should().BeEquivalentTo("modules", "", "data", "data/movie");
    }

    [Fact]
    public void Paths_match_ignoring_case_and_the_existing_casing_wins()
    {
        var instance = Existing(new InstanceFile("x.ifs", @"data\graphic", 10, "OLD.ifs"));

        var plan = UpdatePlanner.Compute(instance, [Source(
            "",
            new SourceFile("Data/Graphic", "X.IFS", 12, "NEW.ifs"),
            new SourceFile("DATA/Sound", "New.2dx", 7, "SOUND.2dx"))]);

        plan.Files.Should().HaveCount(2);
        var replaced = plan.Files.Single(f => f.Status == PlannedStatus.Replaced).File;
        replaced.FileName.Should().Be("x.ifs");
        replaced.RelativePath.Should().Be(@"data\graphic");
        var added = plan.Files.Single(f => f.Status == PlannedStatus.Added).File;
        added.FileName.Should().Be("New.2dx");
        PathNormalizer.Canonicalize(added.RelativePath).Should().Be("data/Sound");
    }

    [Fact]
    public void A_source_lands_below_its_destination()
    {
        var instance = Existing(new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"));

        var plan = UpdatePlanner.Compute(instance, [Source("modules", new SourceFile("", "bm2dx.dll", 120, "NEW.dll"))]);

        Statuses(plan).Should().BeEquivalentTo(new Dictionary<string, PlannedStatus>
        {
            ["modules/bm2dx.dll"] = PlannedStatus.Replaced,
        });
    }

    [Fact]
    public void When_two_sources_hold_the_same_path_the_later_one_wins_and_is_flagged()
    {
        var instance = Existing(new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"));

        var plan = UpdatePlanner.Compute(instance,
        [
            Source("", new SourceFile("modules", "bm2dx.dll", 120, "BASE.dll"), new SourceFile("", "new.txt", 1, "A.txt")),
            Source("", new SourceFile("modules", "bm2dx.dll", 121, "HOTFIX.dll"), new SourceFile("", "new.txt", 2, "B.txt")),
        ]);

        var dll = plan.Files.Single(f => f.File.FileName == "bm2dx.dll");
        dll.Should().BeEquivalentTo(new { Status = PlannedStatus.Replaced, SourceIndex = 1, OverridesEarlierSource = true });
        dll.File.HashedFileName.Should().Be("HOTFIX.dll");
        dll.Previous!.HashedFileName.Should().Be("OLD.dll");
        var txt = plan.Files.Single(f => f.File.FileName == "new.txt");
        txt.Should().BeEquivalentTo(new { Status = PlannedStatus.Added, SourceIndex = 1, OverridesEarlierSource = true });
        txt.File.HashedFileName.Should().Be("B.txt");
    }

    [Fact]
    public void Excluded_files_and_folders_of_a_source_stay_out_of_the_result_but_remain_listed()
    {
        var instance = Existing(new InstanceFile("ea3-config.xml", "prop", 10, "OLD.xml"));

        var plan = UpdatePlanner.Compute(
            instance,
            [new UpdateSource(
                "",
                [
                    new SourceFile("prop", "ea3-config.xml", 11, "NEW.xml"),
                    new SourceFile("data/movie", "a.mp4", 5, "A.mp4"),
                    new SourceFile("data", "keep.bin", 5, "KEEP.bin"),
                ],
                ["data/movie/empty"])],
            excludedPaths: ["PROP/ea3-config.xml", "data/movie"]);

        Statuses(plan).Should().BeEquivalentTo(new Dictionary<string, PlannedStatus>
        {
            ["prop/ea3-config.xml"] = PlannedStatus.Unchanged,
            ["data/movie/a.mp4"] = PlannedStatus.Excluded,
            ["data/keep.bin"] = PlannedStatus.Added,
        });
        plan.Files.Single(f => f.File.FileName == "ea3-config.xml").HasExcludedSource.Should().BeTrue();
        plan.ResultingFiles.Select(f => f.FileName).Should().BeEquivalentTo("ea3-config.xml", "keep.bin");
        plan.Directories.Select(PathNormalizer.Canonicalize).Should().BeEquivalentTo("prop", "data");
        plan.CanApply.Should().BeTrue();
    }

    [Fact]
    public void A_planned_file_points_at_the_source_file_that_supplies_it()
    {
        var supplied = new SourceFile("", "new.bin", 1, "N.bin") { FullPath = "/drop/new.bin" };

        var plan = UpdatePlanner.Compute(Existing(), [Source("", supplied)]);

        plan.Files.Should().ContainSingle().Which.Source.Should().BeSameAs(supplied);
    }

    [Fact]
    public void A_removed_folder_takes_its_files_and_subfolders_but_an_emptied_folder_stays()
    {
        var instance = Instance.Create(
            "IIDX 32",
            [
                new InstanceFile("a.mp4", @"data\movie", 5, "A.mp4"),
                new InstanceFile("b.mp4", @"data\movie\extra", 6, "B.mp4"),
                new InstanceFile("only.tmp", @"data\tmp", 1, "T.tmp"),
                new InstanceFile("moviestar.bin", "data", 2, "M.bin"),
            ],
            ["data", @"data\movie", @"data\movie\extra", @"data\tmp"]);

        var plan = UpdatePlanner.Compute(instance, [], removedPaths: ["data/MOVIE", "data/tmp/only.tmp"]);

        Statuses(plan).Should().BeEquivalentTo(new Dictionary<string, PlannedStatus>
        {
            ["data/movie/a.mp4"] = PlannedStatus.Removed,
            ["data/movie/extra/b.mp4"] = PlannedStatus.Removed,
            ["data/tmp/only.tmp"] = PlannedStatus.Removed,
            ["data/moviestar.bin"] = PlannedStatus.Unchanged,
        });
        plan.ResultingFiles.Should().ContainSingle().Which.FileName.Should().Be("moviestar.bin");
        plan.Directories.Should().Equal("data", @"data\tmp");
        plan.RemovedDirectories.Should().Equal(@"data\movie", @"data\movie\extra");
    }

    [Fact]
    public void A_source_that_fills_a_removed_path_replaces_it_and_brings_the_folder_back()
    {
        var instance = Instance.Create(
            "IIDX 32", [new InstanceFile("a.mp4", @"data\movie", 5, "A.mp4")], ["data", @"data\movie"]);

        var plan = UpdatePlanner.Compute(
            instance,
            [Source("", new SourceFile("data/movie", "a.mp4", 9, "A2.mp4"))],
            removedPaths: ["data/movie"]);

        var file = plan.Files.Should().ContainSingle().Which;
        file.Status.Should().Be(PlannedStatus.Replaced);
        plan.Directories.Select(PathNormalizer.Canonicalize).Should().BeEquivalentTo("data", "data/movie");
    }

    [Fact]
    public void A_folder_that_comes_back_after_its_removal_is_spelled_one_way_for_every_file()
    {
        var instance = Instance.Create(
            "IIDX 32",
            [new InstanceFile("a.mp4", @"data\movie", 5, "A.mp4"), new InstanceFile("b.mp4", @"data\movie", 5, "B.mp4")],
            ["data", @"data\movie"]);

        var plan = UpdatePlanner.Compute(
            instance,
            [Source("",
                new SourceFile("Data/Movie", "new.mp4", 1, "N.mp4"),
                new SourceFile("Data/Movie", "a.mp4", 9, "A2.mp4"),
                new SourceFile("Data/Movie", "b.mp4", 5, "B.mp4"))],
            removedPaths: ["data/movie"]);

        plan.ResultingFiles.Select(f => f.RelativePath).Distinct().Should().ContainSingle();
        plan.Directories.Where(d => PathNormalizer.Canonicalize(d).Equals("data/movie", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle()
            .Which.Should().Be(plan.ResultingFiles.First().RelativePath);
    }

    [Fact]
    public void New_folders_below_a_stored_folder_keep_its_separator_style()
    {
        var instance = Existing(new InstanceFile("x.ifs", @"data\graphic", 10, "X.ifs"));

        var plan = UpdatePlanner.Compute(instance, [Source("", new SourceFile("data/graphic/new/deep", "y.ifs", 1, "Y.ifs"))]);

        plan.Files.Single(f => f.Status == PlannedStatus.Added).File.RelativePath.Should().Be(@"data\graphic\new\deep");
    }

    [Theory]
    [InlineData(false, "data/x.bin")]
    [InlineData(true, "2026091700/data/x.bin")]
    public void A_single_folder_source_merges_into_its_destination_unless_told_to_stay_a_subfolder(
        bool keepTopFolder, string expectedPath)
    {
        var source = new UpdateSource("", [new SourceFile("data", "x.bin", 1, "X.bin")], ["data"])
        {
            TopFolder = "2026091700",
            KeepTopFolder = keepTopFolder,
        };

        var plan = UpdatePlanner.Compute(Existing(), [source]);

        PathOf(plan.Files.Should().ContainSingle().Which).Should().Be(expectedPath);
    }

    [Fact]
    public void An_unhashed_file_at_an_existing_path_is_pending_and_blocks_apply()
    {
        var instance = Existing(new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"));

        var plan = UpdatePlanner.Compute(instance, [Source(
            "", new SourceFile("modules", "bm2dx.dll", 120, null), new SourceFile("modules", "new.dll", 1, null))]);

        Statuses(plan).Should().BeEquivalentTo(new Dictionary<string, PlannedStatus>
        {
            ["modules/bm2dx.dll"] = PlannedStatus.Pending,
            ["modules/new.dll"] = PlannedStatus.Added,
        });
        plan.CanApply.Should().BeFalse();
    }

    [Fact]
    public void An_unhashed_new_file_blocks_apply_too()
    {
        var plan = UpdatePlanner.Compute(Existing(), [Source("", new SourceFile("", "new.dll", 1, null))]);

        plan.Files.Should().ContainSingle().Which.Status.Should().Be(PlannedStatus.Added);
        plan.CanApply.Should().BeFalse();
    }

    [Theory]
    [InlineData("data", "movie", "")]          // a file named like an existing folder
    [InlineData("", "readme.txt", "sub")]      // a folder named like an existing file
    public void A_file_and_a_folder_claiming_one_path_is_a_blocking_collision(
        string directory, string fileName, string below)
    {
        var instance = Instance.Create(
            "IIDX 32",
            [new InstanceFile("a.mp4", @"data\movie", 5, "A.mp4"), new InstanceFile("readme.txt", "", 1, "R.txt")],
            ["data", @"data\movie"]);
        var file = below.Length == 0
            ? new SourceFile(directory, fileName, 1, "X.bin")
            : new SourceFile($"{fileName}/{below}", "x.bin", 1, "X.bin");

        var plan = UpdatePlanner.Compute(instance, [Source("", file)]);

        plan.Collisions.Should().ContainSingle()
            .Which.Path.Should().BeOneOf("data/movie", "readme.txt");
        plan.CanApply.Should().BeFalse();
    }
}
