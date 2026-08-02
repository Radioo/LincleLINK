namespace LincleLINK.App.Abstractions;

/// <summary>App-side theme port so view models can switch light/dark without referencing Avalonia styling.</summary>
public interface IThemeManager
{
    void Apply(bool dark);
}
