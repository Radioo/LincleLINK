using Avalonia.Controls;
using LincleLINK.App.ViewModels.Base;
using Avalonia.Controls.Templates;
using LincleLINK.App.ViewModels;

namespace LincleLINK.App;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        // "LincleLINK.App.ViewModels.AddInstanceViewModel" → "LincleLINK.App.Views.AddInstance"
        // (replacing "ViewModel" with "" also maps the ViewModels namespace to Views).
        var baseName = data.GetType().FullName!
            .Replace("ViewModels.", "Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "", StringComparison.Ordinal);

        var type = Type.GetType(baseName + "View") ?? Type.GetType(baseName + "Window");

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + baseName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}