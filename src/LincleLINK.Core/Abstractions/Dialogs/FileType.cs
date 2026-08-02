namespace LincleLINK.Core.Abstractions.Dialogs;

/// <summary>
/// A file-picker filter: a display label plus one or more glob patterns
/// (e.g. "Torrent files" with "*.torrent"). Replaces a stringly-typed
/// "Label|*.ext;*.ext2" argument so the contract is enforced by the type system.
/// </summary>
/// <param name="Label">Display label shown in the picker's file-type dropdown.</param>
/// <param name="Patterns">File glob patterns, e.g. ["*.torrent"].</param>
public sealed record FileType(string Label, IReadOnlyList<string> Patterns);
