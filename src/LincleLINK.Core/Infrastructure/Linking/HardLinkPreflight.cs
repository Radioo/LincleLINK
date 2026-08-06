using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;

namespace LincleLINK.Core.Infrastructure.Linking;

/// <summary>
/// Probe-based <see cref="IHardLinkPreflight"/>: writes an empty probe file into
/// <c>db/</c>, attempts a real hard link into the target directory, and deletes
/// both. A real link attempt is the only check that is accurate across drive
/// letters, mount points, junctions and bind mounts on every platform.
/// </summary>
public sealed class HardLinkPreflight : IHardLinkPreflight
{
    private readonly IAppPaths _paths;
    private readonly IHardLinker _hardLinker;

    public HardLinkPreflight(IAppPaths paths, IHardLinker hardLinker)
    {
        _paths = paths;
        _hardLinker = hardLinker;
    }

    public string? CheckLinkTo(string directory)
    {
        if (!Directory.Exists(directory))
        {
            // Inconclusive: nothing to probe against; the real operation reports.
            return null;
        }

        var token = Guid.NewGuid().ToString("N");
        var probeSource = Path.Combine(_paths.DbDirectory, $"preflight-{token}.tmp");
        var probeLink = Path.Combine(directory, $".lincle-preflight-{token}.tmp");

        try
        {
            Directory.CreateDirectory(_paths.DbDirectory);
            File.WriteAllBytes(probeSource, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Inconclusive: the probe itself could not be created.
            TryDelete(probeSource);
            return null;
        }

        try
        {
            return _hardLinker.TryCreateLink(probeSource, probeLink, out var error) ? null : error;
        }
        finally
        {
            TryDelete(probeLink);
            TryDelete(probeSource);
        }
    }

    /// <summary>Best-effort probe cleanup (internal for direct testing of the swallow path).</summary>
    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stranded zero-byte probe file is harmless; never fail the check over cleanup.
        }
    }
}
