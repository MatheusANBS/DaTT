using Avalonia.Controls;
using Avalonia.Interactivity;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class CellEditWindow : Window
{
    public CellEditWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CellEditViewModel vm)
            vm.Confirm();

        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void OnFlatClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CellEditViewModel vm)
            vm.TextFormat = JsonViewMode.Flat;
    }

    private void OnVerticalClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CellEditViewModel vm)
            vm.TextFormat = JsonViewMode.Vertical;
    }
}
