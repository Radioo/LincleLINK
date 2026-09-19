namespace LincleLINK.Core.Domain.Updates;

/// <summary>
/// One file of a Source. <see cref="RelativePath"/> is the folder below the
/// Source's Destination. <see cref="HashedFileName"/> is null until the file has
/// been hashed; the plan then can't tell a replacement from an identical file.
/// </summary>
public sealed record SourceFile(string RelativePath, string FileName, long FileSize, string? HashedFileName)
{
    /// <summary>Where the file sits on disk, for hashing and for copying into Storage.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>
    /// Write time seen when the file was scanned. Apply compares it again, since
    /// content copied under a stale hash would corrupt Storage.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; init; }
}

/// <summary>
/// Files the user dropped or picked for an Update, already laid out relative to
/// their Destination folder inside the Instance (empty for the Instance root).
/// </summary>
public sealed record UpdateSource(
    string Destination,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyList<string> Directories)
{
    /// <summary>
    /// Name of the folder when the Source is one dropped folder; null otherwise.
    /// Paths of <see cref="Files"/> and <see cref="Directories"/> are relative to it.
    /// </summary>
    public string? TopFolder { get; init; }

    /// <summary>
    /// The dropped folders on disk. The game detector starts from them when the
    /// Instance itself doesn't hold the files that identify a version. Loose
    /// dropped files add none.
    /// </summary>
    public IReadOnlyList<string> DiskRoots { get; init; } = [];

    /// <summary>
    /// A single-folder Source counts as its Destination, so its contents merge
    /// into it. True keeps the folder as a subfolder instead, as Explorer would.
    /// </summary>
    public bool KeepTopFolder { get; init; }
}

public enum PlannedStatus
{
    /// <summary>In the Instance and untouched by the Pending changes.</summary>
    Unchanged,

    /// <summary>New path from a Source.</summary>
    Added,

    /// <summary>A Source file takes over an existing path with different content.</summary>
    Replaced,

    /// <summary>A Source file matches the existing content at its path, so nothing changes.</summary>
    Identical,

    /// <summary>A Source file at an existing path that has not been hashed yet.</summary>
    Pending,

    /// <summary>Staged for Removal.</summary>
    Removed,

    /// <summary>A Source file at a new path that the user unticked. Listed so it can be ticked again.</summary>
    Excluded,
}

/// <param name="File">The entry as it will be stored, or as it is now for a Removal.</param>
/// <param name="Previous">The entry being replaced or matched; null otherwise.</param>
/// <param name="SourceIndex">Index of the Source that supplies the file, or -1.</param>
/// <param name="OverridesEarlierSource">An earlier Source held the same path and lost to this one.</param>
/// <param name="Source">The Source file that supplies the content, or null.</param>
/// <param name="HasExcludedSource">A Source holds this existing path too, but the user unticked it.</param>
public sealed record PlannedFile(
    InstanceFile File,
    PlannedStatus Status,
    InstanceFile? Previous = null,
    int SourceIndex = -1,
    bool OverridesEarlierSource = false,
    SourceFile? Source = null,
    bool HasExcludedSource = false)
{
    /// <summary>Whether the file is part of the Instance after Apply.</summary>
    public bool IsInResult => Status is not (PlannedStatus.Removed or PlannedStatus.Excluded);
}

/// <summary>A file and a folder claiming the same path. Blocks Apply.</summary>
public sealed record PathCollision(string Path);

/// <param name="Directories">The Instance's directory list after Apply.</param>
/// <param name="RemovedDirectories">Existing directories that a Removal takes out.</param>
public sealed record UpdatePlan(
    IReadOnlyList<PlannedFile> Files,
    IReadOnlyList<string> Directories,
    IReadOnlyList<string> RemovedDirectories,
    IReadOnlyList<PathCollision> Collisions)
{
    /// <summary>Directories among <see cref="Directories"/> that the Sources introduce.</summary>
    public IReadOnlyList<string> AddedDirectories { get; init; } = [];

    /// <summary>False while a path collides or a Source file still waits for its hash.</summary>
    public bool CanApply => Collisions.Count == 0
        && Files.All(f => !f.IsInResult || (f.Status != PlannedStatus.Pending && f.File.HashedFileName.Length > 0));

    /// <summary>The Instance's file list after Apply.</summary>
    public IEnumerable<InstanceFile> ResultingFiles
        => Files.Where(f => f.IsInResult).Select(f => f.File);
}
