using Avalonia.Controls;
using Avalonia.Interactivity;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class CreateTableWindow : Window
{
    public CreateTableWindow()
    {
        InitializeComponent();
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateTableViewModel vm)
            vm.Confirm();

        if (DataContext is CreateTableViewModel { Confirmed: true })
            Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
        => Close();
}
