using MonoTorrent;

namespace LincleLINK.UITests;

/// <summary>
/// On-disk fixtures for the UI tests. The standard source folder has four files
/// (one in a subfolder, two with identical content) so adds exercise nested
/// structure and dedup: 4 library files, 3 unique blobs in storage.
/// </summary>
public static class TestData
{
    public const int SourceFileCount = 4;
    public const int UniqueBlobCount = 3;

    /// <summary>Creates the standard 4-file source folder under <paramref name="root"/>.</summary>
    public static string CreateSourceFolder(string root, string name)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        WriteFile(dir, "alpha.bin", 0xA1, 2048);
        WriteFile(Path.Combine(dir, "sub"), "beta.bin", 0xB2, 3072);
        WriteFile(dir, "dupe1.bin", 0xC3, 1024);
        WriteFile(dir, "dupe2.bin", 0xC3, 1024);
        return dir;
    }

    public static string CreateEmptyFolder(string root, string name)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static byte[] FileContent(byte fill, int size)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, fill);
        return bytes;
    }

    private static void WriteFile(string dir, string name, byte fill, int size)
        => File.WriteAllBytes(Path.Combine(dir, name), FileContent(fill, size));

    /// <summary>
    /// Creates a v1 .torrent of <paramref name="sourceFolder"/> and returns the
    /// torrent path plus the wizard's "relative path" value: the app matches
    /// torrent paths against entry-relative paths, so if MonoTorrent's file paths
    /// carry a folder-name prefix that prefix must be typed into the wizard.
    /// </summary>
    public static async Task<(string TorrentPath, string RelativePath)> CreateTorrentAsync(
        string sourceFolder, string torrentPath)
    {
        var creator = new TorrentCreator(TorrentType.V1Only);
        var dict = await creator.CreateAsync(new TorrentFileSource(sourceFolder));
        await File.WriteAllBytesAsync(torrentPath, dict.Encode());

        var torrent = Torrent.Load(torrentPath);
        var folderName = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar));
        var firstPath = torrent.Files[0].Path.Replace('\\', '/');
        var relativePath = firstPath.StartsWith(folderName + "/", StringComparison.Ordinal)
            ? folderName
            : string.Empty;

        return (torrentPath, relativePath);
    }
}
