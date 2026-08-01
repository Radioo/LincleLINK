using Avalonia;
using Avalonia.Styling;

namespace LincleLINK.App.Services;

public interface IThemeManager
{
    void Apply(bool dark);
}

public sealed class ThemeManager : IThemeManager
{
    public void Apply(bool dark)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
