using CommunityToolkit.Mvvm.ComponentModel;

namespace EukaryLauncher.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual Task OnNavigatedAsync() => Task.CompletedTask;
}
