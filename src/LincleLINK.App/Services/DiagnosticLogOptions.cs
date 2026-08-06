namespace LincleLINK.App.Services;

/// <summary>
/// Resolved once at startup: the on-disk diagnostic log folder (issue #17).
/// Injected into the shell VM so the Settings UI can show the location, create it
/// on enable, and gate the "Open log folder" button on its existence.
/// </summary>
public sealed record DiagnosticLogOptions(string Directory)
{
    public string FilePathPattern => Path.Combine(Directory, "linclelink-.log");
}
