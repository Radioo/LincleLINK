using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.App.Services;

namespace LincleLINK.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IThemeManager _themeManager;

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private double _progress;

    public MainViewModel(IThemeManager themeManager)
    {
        _themeManager = themeManager;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _themeManager.Apply(value);
        LogLines.Add(value ? "Dark mode enabled" : "Dark mode disabled");
    }
}
