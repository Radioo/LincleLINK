namespace LincleLINK.Core.Domain.Validation;

/// <summary>
/// Validates instance names against a platform-stable superset of Windows-illegal
/// names so a data directory can move between Windows and Linux. The invalid
/// character set is hard-coded (not <see cref="Path.GetInvalidFileNameChars"/>),
/// so results are identical on both operating systems.
/// </summary>
public static class InstanceNameValidator
{
    // Windows-invalid filename chars plus both path separators.
    private static readonly char[] InvalidCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    public static bool IsValid(string name) => FirstError(name) is null;

    /// <summary>Returns null if valid, otherwise a human-readable reason.</summary>
    public static string? FirstError(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Instance name cannot be empty.";
        }

        foreach (var c in name)
        {
            if (InvalidCharacters.Contains(c))
            {
                return $"Instance name contains the invalid character '{c}'.";
            }
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "Instance name cannot end with a dot or space.";
        }

        var stem = name.Split('.')[0];
        if (ReservedDeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            return $"Instance name cannot be a reserved device name ('{stem}').";
        }

        return null;
    }
}
