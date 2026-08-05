namespace LincleLINK.Core.Abstractions.Settings;

/// <summary>UI theme preference; <see cref="System"/> follows the OS light/dark setting.</summary>
public enum AppTheme
{
    Light,
    Dark,
    System,
}

/// <summary>
/// App settings stored in the per-OS config directory (never in the data dir).
/// <c>DataDirectory</c> null means "current working directory".
/// <c>HashThreadCount</c> bounds the parallel hashing workers used by add-instance.
/// </summary>
public sealed record AppSettings(AppTheme Theme, string? DataDirectory, int HashThreadCount, LibraryViewMode ViewMode = LibraryViewMode.List);

public enum LibraryViewMode
{
    List,
    Grid,
}

public interface ISettingsStore
{
    /// <summary>True when a settings file exists (used for first-launch detection).</summary>
    bool Exists { get; }

    /// <summary>Tolerant load: missing or corrupt file yields defaults.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
