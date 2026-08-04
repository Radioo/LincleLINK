using FluentAssertions;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Games;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Games;

/// <summary>
/// Real-filesystem checks for <see cref="GameVersionDetector"/> (no mocked IO).
/// Regression: the walk-up from a game's <c>data</c> folder used to throw a
/// NullReferenceException because <c>TryReadEa3Config</c> returns <c>default</c>
/// for a folder without a config, and the caller dereferenced
/// <c>xmlInfo.GameCode.Length</c> on the null value.
/// </summary>
public sealed class GameVersionDetectorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static GameVersionDetector CreateDetector() => new(new FileSystem());

    private const string IidxConfig =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <ea3>
          <id pcbid="0123" />
          <soft model="LDJ" dest="J" spec="A" rev="A" ext="2022101900" />
        </ea3>
        """;

    private const string SdvxConfig =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <ea3>
          <soft model="KFC" dest="J" spec="A" rev="1" ext="2021042800" />
        </ea3>
        """;

    [Fact]
    public async Task Data_folder_walks_up_to_game_root()
    {
        // IIDX layout: game root holds prop/ + bm2dx.dll; instance is root\data.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(IidxConfig));
        _temp.CreateFile("bm2dx.dll", [0x4D, 0x5A, 0, 0]); // MZ header
        var dataFolder = _temp.CreateFile("data/somefile.bin");

        var result = await CreateDetector().DetectAsync(Path.GetDirectoryName(dataFolder)!);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("LDJ");
        result.Info.GameTitle.Should().Be("beatmania IIDX");
        result.Info.DateCode.Should().Be("2022101900");
        result.Info.DisplayTitle.Should().NotBeNullOrWhiteSpace();
        result.GameRootPath.Should().Be(_temp.Root);
        result.IsGameRoot.Should().BeFalse(); // the selected folder is data/, not the root
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Sdvx_contents_data_walks_up_two_levels()
    {
        // SDVX layout: game root holds prop/ + soundvoltex.dll; instance is root\contents\data.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(SdvxConfig));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("contents/data/music.bin");

        var result = await CreateDetector().DetectAsync(Path.Combine(_temp.Root, "contents", "data"));

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.GameTitle.Should().Be("SOUND VOLTEX");
        result.Info.DateCode.Should().Be("2021042800");
        result.DataFolderName.Should().Be(Path.Combine("contents", "data"));
    }

    [Fact]
    public async Task Full_game_root_is_detected_as_game_root()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(IidxConfig));
        _temp.CreateFile("bm2dx.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.IsGameRoot.Should().BeTrue();
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Bootstrap_release_code_overrides_older_ext()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(IidxConfig));
        _temp.CreateFile(
            "prop/bootstrap.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <param>
                  <config><release_code>2023101800</release_code></config>
                </param>
                """));
        _temp.CreateFile("bm2dx.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.DateCode.Should().Be("2023101800"); // newer bootstrap wins
    }

    [Fact]
    public async Task Folder_without_game_files_returns_null_info()
    {
        _temp.CreateFile("some/file.txt");

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().BeNull();
        result.GameRootPath.Should().BeNull();
        result.IsGameRoot.Should().BeFalse();
    }

    [Fact]
    public async Task Unparseable_config_falls_back_to_dll_detection()
    {
        // A config that is present but unreadable XML must not abort detection;
        // the DLL scan still identifies the family.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes("<broken"));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC"); // model hint from the DLL scan
        result.Info.GameTitle.Should().Be("SOUND VOLTEX");
    }

    [Fact]
    public async Task Missing_directory_returns_null_info()
    {
        var missing = Path.Combine(_temp.Root, "does-not-exist");

        var result = await CreateDetector().DetectAsync(missing);

        result.Info.Should().BeNull();
    }
}
