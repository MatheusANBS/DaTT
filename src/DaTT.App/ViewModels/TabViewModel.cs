using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

public abstract partial class TabViewModel : ViewModelBase
{
    public abstract string Title { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;
}
