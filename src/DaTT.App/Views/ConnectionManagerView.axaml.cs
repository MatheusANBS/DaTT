using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class ConnectionManagerView : UserControl
{
    public ConnectionManagerView()
    {
        InitializeComponent();
    }

    private void OnConnectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        // SelectedConnection is already updated by the ListBox binding before DoubleTapped fires
        var mainVm = this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
        mainVm?.ConnectCommand.Execute(null);
    }
}
