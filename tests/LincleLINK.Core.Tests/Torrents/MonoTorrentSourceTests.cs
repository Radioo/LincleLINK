using FluentAssertions;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Infrastructure.Torrents;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Torrents;

public sealed class MonoTorrentSourceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Loads_v1_metadata_from_fixture()
    {
        var files = new[] { ("data/a.bin", new byte[] { 1, 2, 3, 4 }), ("data/b.bin", new byte[] { 5, 6 }) };
        var torrentPath = TorrentTestFactory.CreateTorrentFile(Path.Combine(_temp.Root, "x.torrent"), 4, files);

        var data = await new MonoTorrentSource().LoadAsync(torrentPath);

        data.Name.Should().Be("fixture");
        data.TotalSize.Should().Be(6);
        data.PieceLength.Should().Be(4);
        data.Files.Should().HaveCount(2);
        data.Files[0].FullPath.Should().Be("data/a.bin");
        data.Files[0].Length.Should().Be(4);
        data.Files[1].FullPath.Should().Be("data/b.bin");
        data.Files[1].Length.Should().Be(2);

        var expected = TorrentTestFactory.ComputePieceHashes(files, 4);
        data.PieceHashes.Select(p => Convert.ToHexString(p))
            .Should().Equal(expected.Select(e => Convert.ToHexString(e)));
    }

    [Fact]
    public async Task Missing_file_throws()
    {
        var act = () => new MonoTorrentSource().LoadAsync(Path.Combine(_temp.Root, "nope.torrent"));
        await act.Should().ThrowAsync<Exception>();
    }
}
