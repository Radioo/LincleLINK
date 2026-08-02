using LincleLINK.Core.Abstractions.Linking;

namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>Hard-linker stub for service tests that need to control the result.</summary>
public sealed class TestHardLinker : IHardLinker
{
    public bool Result { get; init; } = true;

    public bool TryCreateLink(string sourcePath, string linkPath, out string? error)
    {
        error = Result ? null : "test error";
        return Result;
    }
}
