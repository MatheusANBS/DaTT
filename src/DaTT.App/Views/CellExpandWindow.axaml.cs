using Avalonia.Controls;
using Avalonia.Interactivity;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class CellExpandWindow : Window
{
    public CellExpandWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void OnFlatClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CellExpandViewModel vm)
            vm.TextFormat = JsonViewMode.Flat;
    }

    private void OnVerticalClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CellExpandViewModel vm)
            vm.TextFormat = JsonViewMode.Vertical;
    }
}
