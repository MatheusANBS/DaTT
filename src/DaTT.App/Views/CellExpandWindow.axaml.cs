using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DaTT.App.Views;

public partial class CellExpandWindow : Window
{
    public CellExpandWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();
}
