namespace LincleLINK.Core.Abstractions.Paths;

/// <summary>
/// Resolves the on-disk data layout. User data stays CWD-relative (v2 parity);
/// the data root is configurable via the settings <c>DataDirectory</c>.
/// </summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string DbDirectory { get; }
    string InstanceDirectory { get; }

    /// <summary>
    /// Creates the data-root layout at startup. Only <c>db/</c> is created eagerly
    /// (the dedup store); since plan 13 moved instance manifests to SQLite, the
    /// <c>instance/</c> folder is created lazily by the legacy JSON migration path.
    /// </summary>
    void EnsureCreated();
}
