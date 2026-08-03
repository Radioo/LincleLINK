namespace LincleLINK.Core.Abstractions.Dialogs;

/// <summary>
/// Outcome of the three-way "files already exist at the target" prompt
/// (plan 14 §3). Dialog dismissal maps to <see cref="Cancel"/>.
/// </summary>
public enum ConflictChoice
{
    /// <summary>Delete the existing files, then link fresh ones in their place.</summary>
    Replace,

    /// <summary>Leave the existing files alone and link only the missing ones.</summary>
    Skip,

    /// <summary>Abort the whole operation.</summary>
    Cancel,
}
