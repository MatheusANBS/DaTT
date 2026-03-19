using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DaTT.App.Views;

internal sealed class ConfirmDialog : Window
{
    private static readonly SolidColorBrush BgBrush = new(Color.Parse("#252526"));
    private static readonly SolidColorBrush FgBrush = new(Color.Parse("#D4D4D4"));
    private static readonly SolidColorBrush AccentBrush = new(Color.Parse("#007ACC"));
    private static readonly SolidColorBrush BtnBgBrush = new(Color.Parse("#3C3C3C"));
    private static readonly SolidColorBrush BtnHoverBrush = new(Color.Parse("#094771"));
    private static readonly SolidColorBrush BorderBrush = new(Color.Parse("#3E3E42"));

    private readonly TaskCompletionSource<bool> _tcs = new();

    private ConfirmDialog(string message)
    {
        Title = "Confirm";
        Width = 400;
        MinHeight = 140;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Background = BgBrush;

        var msgBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FgBrush,
            FontSize = 13,
            Margin = new Thickness(20, 20, 20, 16)
        };

        var yesBtn = new Button
        {
            Content = "Yes",
            Width = 80,
            Height = 30,
            Background = AccentBrush,
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(0)
        };
        yesBtn.Click += (_, _) => { _tcs.TrySetResult(true); Close(); };

        var noBtn = new Button
        {
            Content = "No",
            Width = 80,
            Height = 30,
            Background = BtnBgBrush,
            Foreground = FgBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(2),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1)
        };
        noBtn.Click += (_, _) => { _tcs.TrySetResult(false); Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(20, 0, 20, 20)
        };
        buttons.Children.Add(noBtn);
        buttons.Children.Add(yesBtn);

        var root = new StackPanel();
        root.Children.Add(msgBlock);
        root.Children.Add(buttons);

        Content = root;

        Closed += (_, _) => _tcs.TrySetResult(false);
    }

    public static async Task<bool> ShowAsync(Window owner, string message)
    {
        var dialog = new ConfirmDialog(message);
        await dialog.ShowDialog(owner);
        return await dialog._tcs.Task;
    }
}
