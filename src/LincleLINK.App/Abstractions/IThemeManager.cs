using LincleLINK.Core.Abstractions.Settings;

namespace LincleLINK.App.Abstractions;

/// <summary>App-side theme port so view models can switch themes without referencing Avalonia styling.</summary>
public interface IThemeManager
{
    void Apply(AppTheme theme);
}
