using System.Text.Json;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Infrastructure.Serialization;

/// <summary>
/// Shared JSON options and normalization for instance manifests. Serialization
/// keeps the v2 contract: System.Text.Json default (PascalCase) naming + indented.
/// <see cref="JsonSerializerOptions.RespectNullableAnnotations"/> enforces the
/// domain's non-nullable contract at the deserialization boundary: an explicit
/// <c>null</c> for a non-nullable field fails fast with a JsonException (wrapped by
/// the repository into <c>InstanceStorageException</c>) instead of producing a
/// half-normalized object. Omitted fields still load with their declared defaults,
/// preserving v2 files that lack optional members.
/// </summary>
internal static class InstanceJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
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

        value.InstanceName ??= string.Empty;
        value.TotalFileSizeString ??= string.Empty;
        return value;
    }
}
