namespace LincleLINK.App.Tests.TestHelpers;

/// <summary>
/// Per-test temp directory, cleaned up on Dispose. Mirrors the Core.Tests helper
/// so App tests that write real files (custom logos, import XML) do not leak
/// GUID directories under %TEMP%.
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Root { get; }

    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "LincleLINK.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
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
