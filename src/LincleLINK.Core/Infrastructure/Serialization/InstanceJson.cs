using System.Text.Json;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Infrastructure.Serialization;

/// <summary>
/// Shared JSON options and normalization for instance manifests. Serialization
/// keeps the v2 contract: System.Text.Json default (PascalCase) naming + indented.
/// </summary>
internal static class InstanceJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Guarantees the domain "no null collections" invariant after deserialization.
    /// </summary>
    public static Instance Normalize(Instance value)
    {
        if (value.FileList is null)
        {
            value.FileList = [];
        }

        if (value.DirectoryList is null)
        {
            value.DirectoryList = [];
        }

        value.Name ??= string.Empty;
        value.TotalFileSizeString ??= string.Empty;
        return value;
    }
}
