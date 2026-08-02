using System.Diagnostics.CodeAnalysis;

namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// No-op adapter for platforms without a known shell progress protocol; the
/// in-app progress bar remains the only indicator.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class NullTaskbarProgressBackend : ITaskbarProgressBackend
{
    public void SetValue(double percent)
    {
    }

    public void SetIndeterminate()
    {
    }

    public void Clear()
    {
    }

    public void RequestAttention()
    {
    }
}
