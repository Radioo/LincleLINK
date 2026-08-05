using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Infrastructure.Games;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Games;

/// <summary>
/// IO-fault branches of <see cref="GameVersionDetector"/> reached only through a
/// fault-injecting <see cref="IFileSystem"/>: an unreadable candidate DLL and a
/// DLL whose on-disk bytes vanish between the header probe and the PE read.
/// </summary>
public sealed class GameVersionDetectorIoFaultTests
{
    private const string Config = """
        <?xml version="1.0" encoding="utf-8"?>
        <ea3>
          <soft>
            <model>KFC</model>
            <dest>J</dest>
            <spec>A</spec>
            <rev>A</rev>
            <ext>2013060500</ext>
          </soft>
        </ea3>
        """;

    private static IFileSystem StubBase()
    {
        var fs = Substitute.For<IFileSystem>();
        fs.DirectoryExists(Arg.Any<string>()).Returns(true);
        fs.EnumerateDirectories(Arg.Any<string>(), false).Returns([]);
        fs.EnumerateFiles(Arg.Any<string>(), false).Returns([]);
        fs.FileExists(Arg.Is<string>(p => p != null && Normalized(p).EndsWith("prop/ea3-config.xml", StringComparison.Ordinal)))
            .Returns(true);
        fs.ReadAllText(Arg.Is<string>(p => p != null && Normalized(p).EndsWith("prop/ea3-config.xml", StringComparison.Ordinal)))
            .Returns(Config);
        return fs;
    }

    private static string Normalized(string path) => path.Replace('\\', '/');

    [Fact]
    public async Task Unreadable_dll_is_skipped_by_the_scan()
    {
        var fs = StubBase();
        fs.FileExists(Arg.Is<string>(p => p != null && p.EndsWith("soundvoltex.dll", StringComparison.Ordinal))).Returns(true);
        fs.OpenRead(Arg.Is<string>(p => p != null && p.EndsWith("soundvoltex.dll", StringComparison.Ordinal)))
            .Returns(_ => throw new IOException("locked"));

        var result = await new GameVersionDetector(fs).DetectAsync("C:\\game", TestContext.Current.CancellationToken);

        // Detection falls back to the config; the unreadable dll is simply skipped.
        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
    }

    [Fact]
    public async Task Pe_read_failure_falls_back_to_xml_confidence()
    {
        var fs = StubBase();
        fs.FileExists(Arg.Is<string>(p => p != null && p.EndsWith("soundvoltex.dll", StringComparison.Ordinal))).Returns(true);
        // HasMzHeader passes (MZ magic) but the injected stream is too short to
        // carry a DOS header, so TryReadPeIdentifier (which now reads through the
        // injected IFileSystem) throws and is swallowed.
        fs.OpenRead(Arg.Is<string>(p => p != null && p.EndsWith("soundvoltex.dll", StringComparison.Ordinal)))
            .Returns(_ => new MemoryStream([0x4D, 0x5A, 0, 0]));

        var result = await new GameVersionDetector(fs).DetectAsync("C:\\game", TestContext.Current.CancellationToken);

        result.Info.Should().NotBeNull();
        result.Info!.PeIdentifier.Should().BeNull();
    }

    [Fact]
    public async Task Unreadable_subdirectory_is_skipped_not_aborting_detection()
    {
        // Regression (High 1): the walk-up and data-folder enumerations must
        // degrade when a folder is unreadable (ACL-restricted, missing volume)
        // instead of throwing and aborting the whole detection.
        var fs = StubBase();
        fs.FileExists(Arg.Any<string>()).Returns(false);
        fs.EnumerateDirectories(Arg.Any<string>(), false).Returns(_ => throw new UnauthorizedAccessException("denied"));
        fs.EnumerateFiles(Arg.Any<string>(), false).Returns([]);

        var result = await new GameVersionDetector(fs).DetectAsync("C:\\game", TestContext.Current.CancellationToken);

        result.Info.Should().BeNull();
    }
}
