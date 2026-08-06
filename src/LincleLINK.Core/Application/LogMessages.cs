namespace LincleLINK.Core.Application;

/// <summary>
/// Shared log-line text so wording is defined once: production services/VMs and
/// tests reference these constants instead of duplicating the string. Changing the
/// copy is a single-point edit (wording is cosmetic, not a contract).
/// </summary>
public static class LogMessages
{
    public const string EntryAdded = "Added to library.";
    public const string LibraryRefreshed = "Library refreshed.";
    public const string RelativePathHint = @"Check if your relative path is correct. (example: contents\data)";
    public const string DiagnosticLogEnabledPrefix = "Diagnostic log enabled - saving to";
    public const string DiagnosticLogDisabled = "Diagnostic log disabled";
}
