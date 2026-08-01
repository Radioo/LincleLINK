using CommunityToolkit.Mvvm.ComponentModel;

namespace LincleLINK.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Window title used when this view model is hosted in a dialog window.</summary>
    public virtual string Title => "LincleLINK";
}
