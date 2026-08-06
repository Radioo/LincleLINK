using FluentAssertions;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Infrastructure.Torrents;
using LincleLINK.Core.Tests.TestHelpers;
using MonoTorrent.BEncoding;
using Xunit;

namespace LincleLINK.Core.Tests.Torrents;

/// <summary>
/// Unsupported-format rejection in <see cref="MonoTorrentSource"/>: v2 (BEP 52)
/// torrents and malformed info dictionaries.
/// </summary>
public sealed class MonoTorrentSourceCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteTorrent(BEncodedDictionary root)
    {
        var path = Path.Combine(_temp.Root, Guid.NewGuid().ToString("N") + ".torrent");
        File.WriteAllBytes(path, root.Encode());
        return path;
    }

    private static BEncodedDictionary BuildInfoWithFile(int pieceLength)
    {
        var fileList = new BEncodedList
        {
            new BEncodedDictionary
            {
                [new BEncodedString("length")] = new BEncodedNumber(4),
                [new BEncodedString("path")] = new BEncodedList { new BEncodedString("data"), new BEncodedString("a.bin") },
            },
        };
        return new BEncodedDictionary
        {
            [new BEncodedString("name")] = new BEncodedString("fixture"),
            [new BEncodedString("piece length")] = new BEncodedNumber(pieceLength),
            [new BEncodedString("files")] = fileList,
        };
    }

    [Fact]
    public async Task V2_torrent_is_rejected_with_a_clear_message()
    {
        // A hybrid torrent: valid v1 file/pieces plus the v2 "meta version" marker,
        // so MonoTorrent loads it and reports a v2 info hash.
        var info = BuildInfoWithFile(4);
        info[new BEncodedString("pieces")] = new BEncodedString(new byte[20]);
        info[new BEncodedString("meta version")] = new BEncodedNumber(2);
        var root = new BEncodedDictionary { [new BEncodedString("info")] = info };
        var path = WriteTorrent(root);

        var act = () => new MonoTorrentSource().LoadAsync(path);

        var ex = await act.Should().ThrowAsync<TorrentNotSupportedException>();
        ex.WithMessage("*v2 format*");
    }

    [Fact]
    public async Task Torrent_without_info_dictionary_is_rejected()
    {
        var root = new BEncodedDictionary { [new BEncodedString("announce")] = new BEncodedString("http://x") };
        var path = WriteTorrent(root);

        var act = () => new MonoTorrentSource().LoadAsync(path);

        await act.Should().ThrowAsync<TorrentNotSupportedException>();
    }

    [Fact]
    public async Task Torrent_without_v1_piece_hashes_is_rejected()
    {
        var info = BuildInfoWithFile(4);
        var root = new BEncodedDictionary { [new BEncodedString("info")] = info };
        var path = WriteTorrent(root);

        var act = () => new MonoTorrentSource().LoadAsync(path);

        await act.Should().ThrowAsync<TorrentNotSupportedException>();
    }
}
