using Avalonia.Controls;
using Avalonia.Interactivity;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class SetValuesWindow : Window
{
    public SetValuesWindow()
    {
        InitializeComponent();
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetValuesViewModel vm)
            vm.Confirm();
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
        => Close();
}
