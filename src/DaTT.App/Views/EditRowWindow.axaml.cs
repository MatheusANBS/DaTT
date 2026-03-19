using Avalonia.Controls;
using Avalonia.Interactivity;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class EditRowWindow : Window
{
    public EditRowWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditRowViewModel vm)
            vm.Confirm();
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
        => Close();
}
