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

    // Real EA3 configs express the soft fields as child elements, not attributes.
    private const string IidxConfig =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <ea3>
          <id><pcbid>0123</pcbid></id>
          <soft>
            <model>LDJ</model>
            <dest>J</dest>
            <spec>A</spec>
            <rev>A</rev>
            <ext>2022101900</ext>
          </soft>
        </ea3>
        """;

    private const string SdvxIiConfig =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <ea3>
          <soft>
            <model>KFC</model>
            <dest>J</dest>
            <spec>A</spec>
            <rev>A</rev>
            <ext>2014102201</ext>
          </soft>
        </ea3>
        """;

    [Fact]
    public async Task Data_folder_walks_up_to_game_root()
    {
        // IIDX layout: game root holds prop/ + bm2dx.dll; instance is root\data.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(IidxConfig));
        _temp.CreateFile("bm2dx.dll", [0x4D, 0x5A, 0, 0]); // MZ header
        var dataFolder = _temp.CreateFile("data/graphics/somefile.bin");

        var result = await CreateDetector().DetectAsync(Path.GetDirectoryName(Path.GetDirectoryName(dataFolder))!);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("LDJ");
        result.Info.GameTitle.Should().Be("beatmania IIDX");
        result.Info.DateCode.Should().Be("2022101900");
        result.Info.DisplayTitle.Should().Be("beatmania IIDX 30 RESIDENT");
        result.GameRootPath.Should().Be(_temp.Root);
        result.IsGameRoot.Should().BeFalse(); // the selected folder is data/, not the root
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Unwrapped_sdvx_with_dll_in_modules_is_detected()
    {
        // SDVX II (unwrapped): game root holds prop/ + modules/soundvoltex.dll + data/.
        // modules/ holds many files and must not be mistaken for the data folder.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(SdvxIiConfig));
        for (var i = 0; i < 10; i++)
        {
            _temp.CreateFile($"modules/mod{i}.dll", [0x4D, 0x5A, 0, 0]);
        }

        _temp.CreateFile("modules/soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("data/graphics/a.bin");
        _temp.CreateFile("data/sound/b.bin");
        _temp.CreateFile("data/others/c.bin");

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.GameTitle.Should().Be("SOUND VOLTEX");
        result.Info.DateCode.Should().Be("2014102201");
        result.Info.DisplayTitle.Should().Be("SOUND VOLTEX II -infinite infection-");
        result.Info.LogoKey.Should().Be("SDVX/SDVX_II_logo");
        result.IsGameRoot.Should().BeTrue();
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Unwrapped_sdvx_data_folder_is_detected()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(SdvxIiConfig));
        for (var i = 0; i < 10; i++)
        {
            _temp.CreateFile($"modules/mod{i}.dll", [0x4D, 0x5A, 0, 0]);
        }

        _temp.CreateFile("modules/soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("data/graphics/somefile.bin");

        var result = await CreateDetector().DetectAsync(Path.Combine(_temp.Root, "data"));

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.DisplayTitle.Should().Be("SOUND VOLTEX II -infinite infection-");
        result.IsGameRoot.Should().BeFalse();
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Wrapped_sdvx_inside_contents_is_detected_from_data()
    {
        // Wrapped layout: everything lives under contents/ (prop + modules + data).
        _temp.CreateFile("contents/prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(SdvxIiConfig));
        _temp.CreateFile("contents/modules/soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("contents/data/graphics/somefile.bin");

        var result = await CreateDetector().DetectAsync(Path.Combine(_temp.Root, "contents", "data"));

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.DisplayTitle.Should().Be("SOUND VOLTEX II -infinite infection-");
        result.GameRootPath.Should().Be(Path.Combine(_temp.Root, "contents"));
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Wrapped_sdvx_selected_at_game_root_is_game_root()
    {
        _temp.CreateFile("contents/prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(SdvxIiConfig));
        _temp.CreateFile("contents/modules/soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("contents/data/graphics/somefile.bin");

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.IsGameRoot.Should().BeTrue(); // user selected the folder containing the identity level
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
        // the DLL scan still identifies the family and a model logo.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes("<broken"));
        _temp.CreateFile("modules/soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC"); // model hint from the DLL scan
        result.Info.GameTitle.Should().Be("SOUND VOLTEX");
        result.Info.LogoKey.Should().Be("SDVX/SDVX_BOOTH_logo"); // model fallback logo
        result.Info.DisplayTitle.Should().BeNull(); // no release name; VM falls back to GameTitle
    }

    [Fact]
    public async Task Param_wrapped_config_is_parsed()
    {
        // Attribute-style soft under <param><ea3> must still parse.
        _temp.CreateFile(
            "prop/ea3-config.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <param>
                  <ea3>
                    <soft model="KFC" dest="J" spec="A" rev="1" ext="2013060500" />
                  </ea3>
                </param>
                """));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.DateCode.Should().Be("2013060500");
        result.Info.LogoKey.Should().Be("SDVX/SDVX_II_logo"); // SDVX II datecode range
        result.Info.DisplayTitle.Should().Be("SOUND VOLTEX II -infinite infection-");
    }

    [Fact]
    public async Task Unknown_datecode_still_resolves_model_logo()
    {
        // A datecode that matches no release range must not leave the entry
        // without an icon; the model fallback logo is used instead.
        _temp.CreateFile(
            "prop/ea3-config.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <param>
                  <ea3>
                    <soft model="KFC" dest="J" spec="A" rev="1" ext="2999010100" />
                  </ea3>
                </param>
                """));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await CreateDetector().DetectAsync(_temp.Root);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
        result.Info.LogoKey.Should().Be("SDVX/SDVX_BOOTH_logo"); // fallback, not a specific release
    }

    [Fact]
    public async Task Config_under_prop_defaults_with_config_root_bootstrap()
    {
        // IIDX dumps keep the template ea3-config under prop/defaults and put the
        // authoritative release_code in a <config>-rooted bootstrap.xml.
        _temp.CreateFile(
            "contents/prop/defaults/ea3-config.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <ea3>
                  <soft>
                    <model>LDJ</model>
                    <dest>J</dest>
                    <spec>A</spec>
                    <rev>A</rev>
                    <ext>2010042100</ext>
                  </soft>
                </ea3>
                """));
        _temp.CreateFile(
            "contents/prop/bootstrap.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="shift_jis"?>
                <config>
                  <release_code>2014071600</release_code>
                </config>
                """));
        for (var i = 0; i < 5; i++)
        {
            _temp.CreateFile($"contents/modules/m{i}.dll", [0x4D, 0x5A, 0, 0]);
        }

        _temp.CreateFile("contents/modules/bm2dx.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("contents/data/graphics/a.bin");

        var result = await CreateDetector().DetectAsync(Path.Combine(_temp.Root, "contents", "data"));

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("LDJ");
        result.Info.DateCode.Should().Be("2014071600"); // bootstrap release_code wins over config ext
        result.Info.DisplayTitle.Should().Be("beatmania IIDX 21 SPADA");
        result.Info.LogoKey.Should().Be("IIDX/AC_SPADA_logo");
        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Missing_directory_returns_null_info()
    {
        var missing = Path.Combine(_temp.Root, "does-not-exist");

        var result = await CreateDetector().DetectAsync(missing);

        result.Info.Should().BeNull();
    }
}
