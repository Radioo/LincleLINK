using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Semi.Avalonia;

namespace LincleLINK.App.Services;

/// <summary>
/// Recolors Semi's primary palette from the stock blue to the LincleLINK logo
/// orange (#F6AB00). Semi aliases its tokens with StaticResource, so shadowing
/// them via Application.Resources has no effect on already-resolved aliases;
/// instead the palette brush instances are recolored in place, which every
/// alias shares. Must run before any window is created.
/// </summary>
public static class BrandTheme
{
    private readonly record struct TokenColors(Color Light, Color Dark);

    private static readonly Dictionary<string, TokenColors> Tokens = new()
    {
        ["SemiColorPrimary"] = new(Color.Parse("#F6AB00"), Color.Parse("#FFBE29")),
        ["SemiColorPrimaryPointerover"] = new(Color.Parse("#D18F00"), Color.Parse("#FFCC57")),
        ["SemiColorPrimaryActive"] = new(Color.Parse("#AD7400"), Color.Parse("#FFDA85")),
        ["SemiColorPrimaryDisabled"] = new(Color.Parse("#FFDD8F"), Color.Parse("#946800")),
        ["SemiColorPrimaryLight"] = new(Color.Parse("#FFF8E5"), Color.Parse("#FFBE29")),
        ["SemiColorPrimaryLightPointerover"] = new(Color.Parse("#FFEDBF"), Color.Parse("#FFBE29")),
        ["SemiColorPrimaryLightActive"] = new(Color.Parse("#FFDD8F"), Color.Parse("#FFBE29")),
        ["SemiColorFocusBorder"] = new(Color.Parse("#F6AB00"), Color.Parse("#FFBE29")),
        ["SemiColorLink"] = new(Color.Parse("#F6AB00"), Color.Parse("#FFBE29")),
        ["SemiColorLinkPointerover"] = new(Color.Parse("#D18F00"), Color.Parse("#FFCC57")),
        ["SemiColorLinkActive"] = new(Color.Parse("#AD7400"), Color.Parse("#FFDA85")),
        ["SemiColorLinkVisited"] = new(Color.Parse("#F6AB00"), Color.Parse("#FFBE29")),
    };

    public static void Apply(Application app)
    {
        foreach (var style in app.Styles)
        {
            if (style is not SemiTheme { Resources: ResourceDictionary resources })
            {
                continue;
            }

            foreach (var (key, colors) in Tokens)
            {
                Recolor(resources, ThemeVariant.Default, key, colors.Light);
                Recolor(resources, ThemeVariant.Light, key, colors.Light);
                Recolor(resources, ThemeVariant.Dark, key, colors.Dark);
            }
        }
    }

    private static void Recolor(ResourceDictionary resources, ThemeVariant variant, string key, Color target)
    {
        if (resources.TryGetResource(key, variant, out var value) && value is SolidColorBrush brush)
        {
            // Keep the palette's own alpha: the dark variant's "PrimaryLight"
            // fills are translucent.
            brush.Color = new Color(brush.Color.A, target.R, target.G, target.B);
        }
    }
}
