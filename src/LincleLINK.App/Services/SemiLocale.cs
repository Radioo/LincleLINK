using System.Globalization;
using Avalonia;
using Semi.Avalonia;

namespace LincleLINK.App.Services;

/// <summary>
/// Points Semi's built-in control strings (the TextBox context menu, etc.) at
/// the Windows display language. Semi never reads the system culture: when
/// <c>SemiTheme.Locale</c> is unset it stays on its Simplified-Chinese default,
/// and its own lookup also falls back to zh-CN for unsupported cultures. The
/// culture is therefore mapped onto Semi's shipped locale set here, with
/// English as the final fallback. Must run before any window is created.
/// </summary>
public static class SemiLocale
{
    // The locale dictionaries shipped in Semi.Avalonia 12.1, representative
    // culture first per language so the prefix match below picks it.
    private static readonly string[] Supported =
    [
        "zh-CN", "zh-TW", "en-US", "en-GB", "ja-JP", "ko-KR", "de-DE", "es-ES",
        "fr-FR", "it-IT", "it-CH", "nl-NL", "nl-BE", "pl-PL", "ru-RU", "uk-UA",
    ];

    public static void Apply(Application app)
    {
        // CurrentUICulture follows the Windows display language, not the
        // regional/locale format settings.
        var culture = Resolve(CultureInfo.CurrentUICulture);
        foreach (var style in app.Styles)
        {
            if (style is SemiTheme theme)
            {
                theme.Locale = culture;
            }
        }
    }

    private static CultureInfo Resolve(CultureInfo ui)
    {
        var exact = Supported.FirstOrDefault(
            n => string.Equals(n, ui.Name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new CultureInfo(exact);
        }

        // en-AU → en-US, ja → ja-JP, and so on; unknown languages get English.
        var prefix = ui.TwoLetterISOLanguageName + "-";
        var sameLanguage = Supported.FirstOrDefault(
            n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return new CultureInfo(sameLanguage ?? "en-US");
    }
}
