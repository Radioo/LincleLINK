using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace LincleLINK.App.Services;

/// <summary>
/// Pure Markdown formatter for the error-report window (issue #16 D3). Static and
/// side-effect-free so it is unit-testable and reusable by the fatal path's
/// console fallback. The output is a GitHub-issue-ready block: a "What I was
/// doing" placeholder prompts the reporter, and the fenced exception keeps long
/// stack traces collapsed.
/// </summary>
public static class ExceptionReport
{
    /// <summary>
    /// The values shown in the report window's environment strip; the same values
    /// that go into the Markdown report so the two never drift apart.
    /// </summary>
    public static string EnvironmentLine =>
        $"LincleLINK {EntryVersion()} · {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}) · {RuntimeInformation.FrameworkDescription} · Avalonia {AvaloniaVersion()}";

    public static string Format(Exception exception, DateTimeOffset utcNow, int occurrences)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Crash report");
        sb.AppendLine();
        sb.AppendLine($"- **Version:** {EntryVersion()}");
        sb.AppendLine($"- **OS:** {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"- **Runtime:** {RuntimeInformation.FrameworkDescription} · Avalonia {AvaloniaVersion()}");
        sb.AppendLine($"- **When (UTC):** {utcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Occurrences:** {occurrences}");
        sb.AppendLine();
        sb.AppendLine("**What I was doing:**");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(exception.ToString());
        sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Informational version of the entry assembly, without the build metadata suffix.</summary>
    private static string EntryVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly?.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string AvaloniaVersion()
    {
        var assembly = typeof(Application).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrEmpty(informational)
            ? assembly.GetName().Version?.ToString(3) ?? "unknown"
            : informational;
    }
}
