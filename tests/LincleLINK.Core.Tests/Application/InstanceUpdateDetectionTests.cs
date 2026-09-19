using System.Text;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Games;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Game detection for an Update (plan 16 D10), with the real detector over real
/// folders: first over the Instance as the Pending changes leave it, read through
/// Storage, then over the dropped folder on disk.
/// </summary>
public sealed class InstanceUpdateDetectionTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FileSystem _fs = new();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private Instance? _saved;

    public InstanceUpdateDetectionTests()
    {
        // Storage is a temp folder: a hashed name resolves to a file in it.
        _store.GetPath(Arg.Any<string>()).Returns(call => Path.Combine(_temp.Root, "db", call.Arg<string>()!));
        _store.Exists(Arg.Any<string>()).Returns(call => File.Exists(Path.Combine(_temp.Root, "db", call.Arg<string>()!)));
        _repository.SaveAsync(Arg.Do<Instance>(i => _saved = i), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _temp.Dispose();

    private InstanceUpdateService CreateService()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DbDirectory.Returns(Path.Combine(_temp.Root, "db"));
        var driveInfo = Substitute.For<IDriveInfoProvider>();
        driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(long.MaxValue);
        return new InstanceUpdateService(
            _fs, Substitute.For<LincleLINK.Core.Abstractions.Hashing.IFileHasher>(), _store, _repository, driveInfo, paths,
            Substitute.For<IDialogService>(), new GameVersionDetector(_fs), NullLogger<InstanceUpdateService>.Instance);
    }

    private static byte[] Config(string model, string ext) => Encoding.UTF8.GetBytes(
        $"<ea3><soft><model>{model}</model><dest>J</dest><spec>A</spec><rev>A</rev><ext>{ext}</ext></soft></ea3>");

    private static GameVersionInfo Game(string code, string dateCode)
        => new(code, "whatever", dateCode, null, null, null, DetectionConfidence.Xml);

    /// <summary>A full-game-folder entry: its config sits in Storage under a hashed name.</summary>
    private Instance FullGameEntry(string ext)
    {
        _temp.CreateFile("db/OLDCFG.xml", Config("LDJ", ext));
        var instance = Instance.Create(
            "IIDX 32",
            [new InstanceFile("ea3-config.xml", "prop", 10, "OLDCFG.xml"), new InstanceFile("x.ifs", @"data\graphic", 1, "X.ifs")],
            ["prop", "data", @"data\graphic"]);
        instance.DetectedGame = Game("LDJ", ext);
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    /// <summary>The usual entry: only the data folder's contents, no config of its own.</summary>
    private Instance DataOnlyEntry(GameVersionInfo? detected)
    {
        var instance = Instance.Create("IIDX 32", [new InstanceFile("x.ifs", "graphic", 1, "X.ifs")], ["graphic"]);
        instance.DetectedGame = detected;
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private async Task<PendingChanges> DropAsync(string folder)
    {
        var scan = await CreateService().ScanAsync([folder], TestContext.Current.CancellationToken);
        var hashed = scan.Source with
        {
            Files = scan.Source.Files.Select(f => f with { HashedFileName = $"NEW{f.FileName.Length}{Path.GetExtension(f.FileName)}" }).ToList(),
        };
        return new PendingChanges([hashed], [], []);
    }

    [Fact]
    public async Task A_replaced_config_inside_the_entry_is_detected_through_the_pending_changes()
    {
        var instance = FullGameEntry("2026031800");
        _temp.CreateFile("pack/prop/ea3-config.xml", Config("LDJ", "2026091700"));
        var changes = await DropAsync(Path.Combine(_temp.Root, "pack"));

        var change = await CreateService().DetectVersionChangeAsync(instance, changes, TestContext.Current.CancellationToken);

        change.Should().NotBeNull();
        change!.Current!.DateCode.Should().Be("2026031800");
        change.Detected.DateCode.Should().Be("2026091700");
        change.Detected.GameCode.Should().Be("LDJ");
        change.FromDroppedFolder.Should().BeFalse();
    }

    [Fact]
    public async Task A_data_only_entry_takes_the_version_from_next_to_the_dropped_folder()
    {
        var instance = DataOnlyEntry(Game("LDJ", "2026031800"));
        _temp.CreateFile("LDJ-2026091700/contents/prop/ea3-config.xml", Config("LDJ", "2026091700"));
        _temp.CreateFile("LDJ-2026091700/contents/data/graphic/x.ifs", [1, 2]);
        var changes = await DropAsync(Path.Combine(_temp.Root, "LDJ-2026091700", "contents", "data"));

        var change = await CreateService().DetectVersionChangeAsync(instance, changes, TestContext.Current.CancellationToken);

        change.Should().NotBeNull();
        change!.Detected.DateCode.Should().Be("2026091700");
        change.FromDroppedFolder.Should().BeTrue();
    }

    [Fact]
    public async Task A_dropped_folder_of_another_game_changes_nothing()
    {
        var instance = DataOnlyEntry(Game("LDJ", "2026031800"));
        _temp.CreateFile("sdvx/prop/ea3-config.xml", Config("KFC", "2026091700"));
        _temp.CreateFile("sdvx/data/x.bin", [1]);
        var changes = await DropAsync(Path.Combine(_temp.Root, "sdvx", "data"));

        var change = await CreateService().DetectVersionChangeAsync(instance, changes, TestContext.Current.CancellationToken);

        change.Should().BeNull();
    }

    [Fact]
    public async Task Nothing_detected_or_nothing_new_changes_nothing()
    {
        var instance = FullGameEntry("2026031800");
        _temp.CreateFile("plain/data/graphic/y.ifs", [1]);
        var changes = await DropAsync(Path.Combine(_temp.Root, "plain"));

        var sameVersion = await CreateService().DetectVersionChangeAsync(instance, changes, TestContext.Current.CancellationToken);
        var nothingFound = await CreateService().DetectVersionChangeAsync(
            DataOnlyEntry(Game("LDJ", "2026031800")), changes, TestContext.Current.CancellationToken);

        // The entry's own config still says 2026031800; a data-only entry with an
        // unrecognizable drop keeps what it has instead of losing its detection.
        sameVersion.Should().BeNull();
        nothingFound.Should().BeNull();
    }

    [Theory]
    [InlineData("LDJ")]
    [InlineData("TDJ")] // the DLL's name only hints at LDJ; the tag knows better
    public async Task A_detection_without_a_date_code_does_not_replace_a_known_one(string taggedAs)
    {
        // The entry loses its config through a Removal; its game DLL alone still
        // names the game, but no longer the version.
        var instance = FullGameEntry("2026031800");
        instance.DetectedGame = Game(taggedAs, "2026031800");
        _temp.CreateFile("db/GAME.dll", [(byte)'M', (byte)'Z', 0, 0]);
        instance.FileList.Add(new InstanceFile("bm2dx.dll", "modules", 4, "GAME.dll"));
        var changes = new PendingChanges([], [], ["prop"]);

        var change = await CreateService().DetectVersionChangeAsync(instance, changes, TestContext.Current.CancellationToken);

        change.Should().BeNull();
    }

    [Fact]
    public async Task Changes_that_touch_nothing_a_game_is_identified_by_detect_nothing()
    {
        var instance = FullGameEntry("2026031800");
        instance.DetectedGame = Game("LDJ", "an older tag that a detection would replace");

        var change = await CreateService().DetectVersionChangeAsync(
            instance, new PendingChanges([], [], ["data/graphic/x.ifs"]), TestContext.Current.CancellationToken);

        change.Should().BeNull();
    }

    [Fact]
    public async Task A_source_whose_files_are_all_unticked_is_not_asked_about_the_version()
    {
        var instance = DataOnlyEntry(Game("LDJ", "2026031800"));
        _temp.CreateFile("LDJ-2026091700/prop/ea3-config.xml", Config("LDJ", "2026091700"));
        _temp.CreateFile("LDJ-2026091700/data/new.ifs", [1, 2]);
        var dropped = await DropAsync(Path.Combine(_temp.Root, "LDJ-2026091700", "data"));

        var change = await CreateService().DetectVersionChangeAsync(
            instance, dropped with { ExcludedPaths = ["new.ifs"] }, TestContext.Current.CancellationToken);

        change.Should().BeNull();
    }

    [Fact]
    public async Task A_loose_dropped_file_gives_no_folder_to_detect_from()
    {
        _temp.CreateFile("downloads/some-game/prop/ea3-config.xml", Config("LDJ", "2026091700"));
        var file = _temp.CreateFile("downloads/patch.bin", [1]);

        var scan = await CreateService().ScanAsync([file], TestContext.Current.CancellationToken);

        scan.Source.DiskRoots.Should().BeEmpty();
    }

    [Fact]
    public void The_detector_names_the_files_it_identifies_a_game_by()
    {
        var detector = new GameVersionDetector(_fs);

        detector.IsIdentityFile("EA3-Config.xml").Should().BeTrue();
        detector.IsIdentityFile("bootstrap.xml").Should().BeTrue();
        detector.IsIdentityFile("bm2dx.dll").Should().BeTrue();
        detector.IsIdentityFile("x.ifs").Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "2026091700")]
    [InlineData(false, "2026031800")]
    public async Task Apply_tags_the_entry_with_the_version_the_user_accepted(bool accepted, string expected)
    {
        FullGameEntry("2026031800");
        _temp.CreateFile("pack/prop/ea3-config.xml", Config("LDJ", "2026091700"));
        var changes = await DropAsync(Path.Combine(_temp.Root, "pack"));
        changes = changes with { DetectedGame = accepted ? Game("LDJ", "2026091700") : null };
        Directory.CreateDirectory(Path.Combine(_temp.Root, "db"));
        _store.CopyToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        _saved!.DetectedGame!.DateCode.Should().Be(expected);
        _saved.DetectedGame.GameCode.Should().Be("LDJ");
    }
}
