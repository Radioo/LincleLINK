namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>
/// Per-test temp directory; cleaned up on Dispose. Every filesystem-backed test
/// should construct its own instance.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Root { get; }

    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "LincleLINK.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Creates a file (and its parent dirs) under the temp root, returning its path.</summary>
    public string CreateFile(string relativePath, byte[]? contents = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents ?? []);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e)
        {
            // Best-effort cleanup; surface so a stuck temp dir is not invisible.
            Console.Error.WriteLine($"Failed to clean up temp dir {Root}: {e.Message}");
        }
    }
}
