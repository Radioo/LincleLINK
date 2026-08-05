using FluentAssertions;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Infrastructure.Games;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Games;

/// <summary>
/// PE identifier extraction and DLL-scan edge cases in <see cref="GameVersionDetector"/>,
/// exercised with hand-built PE binaries.
/// </summary>
public sealed class GameVersionDetectorPeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

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

    private static void WriteUInt16(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] buf, int offset, uint value)
    {
        for (var i = 0; i < 4; i++)
        {
            buf[offset + i] = (byte)(value >> (8 * i));
        }
    }

    /// <summary>A minimal valid PE32 image with the given time-date-stamp and entry point.</summary>
    private static byte[] BuildPe(uint timeDateStamp, uint entryPoint, int sizeOfOptionalHeader = 0xE0)
    {
        var buf = new byte[0x200];
        buf[0] = 0x4D; // "MZ"
        buf[1] = 0x5A;
        const int peOffset = 0x80;
        WriteUInt32(buf, 0x3C, peOffset);
        WriteUInt32(buf, peOffset, 0x00004550); // "PE\0\0"
        var hdr = peOffset + 4;
        WriteUInt16(buf, hdr, 0x014C);      // machine I386
        WriteUInt16(buf, hdr + 2, 1);       // NumberOfSections
        WriteUInt32(buf, hdr + 4, timeDateStamp);
        WriteUInt32(buf, hdr + 8, 0);       // PointerToSymbolTable
        WriteUInt32(buf, hdr + 12, 0);      // NumberOfSymbols
        WriteUInt16(buf, hdr + 16, (ushort)sizeOfOptionalHeader);
        WriteUInt16(buf, hdr + 18, 0x0102); // Characteristics
        var opt = hdr + 20;
        WriteUInt16(buf, opt, 0x010B);      // magic PE32
        WriteUInt32(buf, opt + 16, entryPoint);
        return buf;
    }

    [Fact]
    public async Task Valid_pe_produces_an_identifier_and_xml_and_pe_confidence()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        _temp.CreateFile("soundvoltex.dll", BuildPe(0x5A01C0A8, 0x1000));

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info.Should().NotBeNull();
        result.Info!.PeIdentifier.Should().Be("kfc-5a01c0a8_1000");
        result.Info.Confidence.Should().Be(DetectionConfidence.XmlAndPe);
    }

    [Fact]
    public async Task Dll_shorter_than_the_dos_header_yields_no_identifier()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info.Should().NotBeNull();
        result.Info!.PeIdentifier.Should().BeNull();
        result.Info.Confidence.Should().Be(DetectionConfidence.Xml);
    }

    [Fact]
    public async Task Invalid_pe_signature_yields_no_identifier()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        // MZ header with a valid e_lfanew but a bogus PE signature.
        var buf = BuildPe(0x5A01C0A8, 0x1000);
        WriteUInt32(buf, 0x80, 0xDEADBEEF);
        _temp.CreateFile("soundvoltex.dll", buf);

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info!.PeIdentifier.Should().BeNull();
        result.Info.Confidence.Should().Be(DetectionConfidence.Xml);
    }

    [Fact]
    public async Task Optional_header_smaller_than_twenty_yields_no_identifier()
    {
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        _temp.CreateFile("soundvoltex.dll", BuildPe(0x5A01C0A8, 0x1000, sizeOfOptionalHeader: 10));

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info!.PeIdentifier.Should().BeNull();
    }

    [Fact]
    public async Task Nvram_folder_is_treated_as_support_not_data()
    {
        // A sibling folder containing nvram/ must not be picked as the data folder.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);
        _temp.CreateFile("data/graphics/a.bin");
        _temp.CreateFile("app/nvram/config.bin");

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.DataFolderName.Should().Be("data");
    }

    [Fact]
    public async Task Soft_node_without_model_is_skipped()
    {
        // A config whose <soft> has no model attribute/element must be skipped.
        _temp.CreateFile(
            "prop/ea3-config.xml",
            System.Text.Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="utf-8"?>
                <ea3>
                  <soft>
                    <dest>J</dest>
                  </soft>
                </ea3>
                """));

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info.Should().BeNull();
    }

    [Fact]
    public async Task Unparseable_bootstrap_is_non_fatal()
    {
        // The config is valid but bootstrap.xml is malformed; detection still succeeds.
        _temp.CreateFile("prop/ea3-config.xml", System.Text.Encoding.UTF8.GetBytes(Config));
        _temp.CreateFile("prop/bootstrap.xml", System.Text.Encoding.UTF8.GetBytes("<broken"));
        _temp.CreateFile("soundvoltex.dll", [0x4D, 0x5A, 0, 0]);

        var result = await new GameVersionDetector(new FileSystem()).DetectAsync(_temp.Root, TestContext.Current.CancellationToken);

        result.Info.Should().NotBeNull();
        result.Info!.GameCode.Should().Be("KFC");
    }
}
