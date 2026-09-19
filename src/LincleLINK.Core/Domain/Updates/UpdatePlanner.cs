namespace LincleLINK.Core.Domain.Updates;

/// <summary>
/// Computes what an Instance looks like after its Pending changes apply (plan 16
/// D12). Pure: no disk, no Storage. Paths match case-insensitively on every
/// platform and the Instance's existing casing wins (ADR 0001).
/// </summary>
public static class UpdatePlanner
{
    /// <param name="excludedPaths">Resulting paths (file or folder) the user unticked in the preview.</param>
    /// <param name="removedPaths">Existing paths (file or folder) staged for Removal.</param>
    public static UpdatePlan Compute(
        Instance instance,
        IReadOnlyList<UpdateSource> sources,
        IEnumerable<string>? excludedPaths = null,
        IEnumerable<string>? removedPaths = null)
    {
        var excluded = (excludedPaths ?? []).Select(Key).ToList();
        var removed = (removedPaths ?? []).Select(Key).ToList();

        var directories = new DirectoryCasing();
        var keptDirectories = new List<string>();
        var droppedDirectories = new List<string>();
        foreach (var directory in instance.DirectoryList)
        {
            if (IsWithinAny(Key(directory), removed))
            {
                droppedDirectories.Add(directory);
            }
            else
            {
                directories.Register(directory);
                keptDirectories.Add(directory);
            }
        }

        // Existing files first, in their stored order, so the result keeps it.
        var planned = new List<PlannedFile>(instance.FileList.Count);
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in instance.FileList)
        {
            var key = Key(file.RelativePath, file.FileName);
            var isRemoved = IsWithinAny(key, removed);
            if (!isRemoved)
            {
                directories.Register(file.RelativePath);
            }

            indexByKey[key] = planned.Count;
            planned.Add(new PlannedFile(file, isRemoved ? PlannedStatus.Removed : PlannedStatus.Unchanged));
        }

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            var destination = source is { KeepTopFolder: true, TopFolder: { } topFolder }
                ? Combine(source.Destination, topFolder)
                : PathNormalizer.Canonicalize(source.Destination);
            if (source.KeepTopFolder && !IsWithinAny(Key(destination), excluded))
            {
                directories.Resolve(destination);
            }

            foreach (var directory in source.Directories)
            {
                var target = Combine(destination, directory);
                if (!IsWithinAny(Key(target), excluded))
                {
                    directories.Resolve(target);
                }
            }

            foreach (var file in source.Files)
            {
                var target = Combine(destination, file.RelativePath);
                var key = Key(target, file.FileName);
                var known = indexByKey.TryGetValue(key, out var index);
                if (IsWithinAny(key, excluded))
                {
                    // Stays listed so the user can tick it again, but adds nothing:
                    // no entry in the result and no folder of its own.
                    var listed = new PlannedFile(
                        new InstanceFile(file.FileName, target, file.FileSize, file.HashedFileName ?? string.Empty),
                        PlannedStatus.Excluded,
                        SourceIndex: sourceIndex,
                        Source: file);
                    if (!known)
                    {
                        indexByKey[key] = planned.Count;
                        planned.Add(listed);
                    }
                    else if (planned[index].Status == PlannedStatus.Excluded)
                    {
                        planned[index] = listed;
                    }
                    else
                    {
                        planned[index] = planned[index] with { HasExcludedSource = true };
                    }
                }
                else if (known)
                {
                    planned[index] = Overlay(planned[index], file, sourceIndex, directories, target);
                }
                else
                {
                    indexByKey[key] = planned.Count;
                    planned.Add(new PlannedFile(
                        new InstanceFile(file.FileName, directories.Resolve(target), file.FileSize, file.HashedFileName ?? string.Empty),
                        PlannedStatus.Added,
                        SourceIndex: sourceIndex,
                        Source: file));
                }
            }
        }

        var resultingDirectories = keptDirectories.Concat(directories.Added).ToList();

        // A Source can bring a removed folder back, and then it is not removed.
        var removedDirectories = droppedDirectories.Where(d => !directories.Contains(Key(d))).ToList();
        return new UpdatePlan(planned, resultingDirectories, removedDirectories, FindCollisions(planned, directories))
        {
            AddedDirectories = directories.Added,
        };
    }

    /// <summary>A Source file lands on a path that is already planned: an existing entry or an earlier Source's file.</summary>
    private static PlannedFile Overlay(
        PlannedFile current, SourceFile file, int sourceIndex, DirectoryCasing directories, string target)
    {
        // The casing table is the one authority on how a folder is spelled: the
        // Instance's spelling while the folder exists (ADR 0001), or the Source's
        // when a Removal took the folder out and this file brings it back.
        var resolvedDirectory = directories.Resolve(target);

        var fromEarlierSource = current.SourceIndex >= 0;

        // What the Instance holds at this path today, if anything. A Removal that a
        // Source fills again is a replacement of the old entry, not a removal.
        var existing = fromEarlierSource ? current.Previous : current.File;
        if (existing is null)
        {
            return new PlannedFile(
                new InstanceFile(file.FileName, resolvedDirectory, file.FileSize, file.HashedFileName ?? string.Empty),
                PlannedStatus.Added,
                SourceIndex: sourceIndex,
                OverridesEarlierSource: true,
                Source: file);
        }

        var status = file.HashedFileName is null
            ? PlannedStatus.Pending
            : string.Equals(file.HashedFileName, existing.HashedFileName, StringComparison.OrdinalIgnoreCase)
                ? PlannedStatus.Identical
                : PlannedStatus.Replaced;

        // Existing casing wins for the replaced file's name (ADR 0001).
        var entry = status == PlannedStatus.Identical
            ? existing with { RelativePath = resolvedDirectory }
            : new InstanceFile(existing.FileName, resolvedDirectory, file.FileSize, file.HashedFileName ?? string.Empty);
        return new PlannedFile(entry, status, existing, sourceIndex, fromEarlierSource, file);
    }

    private static List<PathCollision> FindCollisions(List<PlannedFile> planned, DirectoryCasing directories)
    {
        var collisions = new List<PathCollision>();
        foreach (var file in planned)
        {
            if (file.IsInResult
                && directories.Contains(Key(file.File.RelativePath, file.File.FileName)))
            {
                collisions.Add(new PathCollision(
                    PathNormalizer.Canonicalize($"{file.File.RelativePath}/{file.File.FileName}")));
            }
        }

        return collisions;
    }

    private static string Combine(string destination, string relativePath)
        => PathNormalizer.Canonicalize($"{destination}/{relativePath}");

    /// <summary>Case-insensitive matching key of a path: canonical separators, upper-cased.</summary>
    private static string Key(string path) => PathNormalizer.Canonicalize(path).ToUpperInvariant();

    private static string Key(string directory, string fileName) => Key($"{directory}/{fileName}");

    private static bool IsWithinAny(string key, List<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            // An empty prefix would match everything; a staged path is never the root.
            if (prefix.Length > 0 && PathNormalizer.IsWithin(key, prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stored paths carry the separator of whoever wrote them (v2 and Windows use
    /// a backslash, other hosts '/'). A new folder continues its parent's style so
    /// one stored path never mixes both.
    /// </summary>
    private static char SeparatorOf(string storedParent)
        => storedParent.Contains('\\') ? '\\'
            : storedParent.Contains('/') ? '/'
            : Path.DirectorySeparatorChar;

    /// <summary>
    /// The casing every directory of the result is stored with. Known directories
    /// keep theirs; a new one takes the casing it arrives with, below the known
    /// casing of its parent.
    /// </summary>
    private sealed class DirectoryCasing
    {
        private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal) { [string.Empty] = string.Empty };

        /// <summary>Directories that the Sources introduced, parents before children.</summary>
        public List<string> Added { get; } = [];

        public bool Contains(string key) => key.Length > 0 && _byKey.ContainsKey(key);

        /// <summary>Records a stored directory and its ancestors exactly as the Instance spells them.</summary>
        public void Register(string storedPath)
        {
            var canonical = PathNormalizer.Canonicalize(storedPath);
            var end = canonical.Length;
            var stored = storedPath;
            while (end > 0)
            {
                _byKey.TryAdd(canonical[..end].ToUpperInvariant(), stored);
                end = canonical.LastIndexOf('/', end - 1);
                if (end < 0)
                {
                    break;
                }

                stored = PathNormalizer.ToPlatformSeparators(canonical[..end]);
            }
        }

        /// <summary>Returns how to store a directory, adding it and any missing parents to the result.</summary>
        public string Resolve(string canonicalPath)
        {
            var key = canonicalPath.ToUpperInvariant();
            if (_byKey.TryGetValue(key, out var known))
            {
                return known;
            }

            var split = canonicalPath.LastIndexOf('/');
            var parent = split < 0 ? string.Empty : Resolve(canonicalPath[..split]);
            var name = canonicalPath[(split + 1)..];
            var resolved = parent.Length == 0 ? name : parent + SeparatorOf(parent) + name;
            _byKey[key] = resolved;
            Added.Add(resolved);
            return resolved;
        }
    }
}
