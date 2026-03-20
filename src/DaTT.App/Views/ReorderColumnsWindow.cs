using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace DaTT.App.Views;

internal sealed class ReorderColumnsWindow : Window
{
    private const double ItemHeight = 40;
    private const double ItemSpacing = 3;

    private readonly List<string> _columns;
    private readonly List<Border> _itemBorders = [];
    private readonly StackPanel _itemsPanel;
    private int _dragFromIndex = -1;

    public bool Confirmed { get; private set; }
    public IReadOnlyList<string> OrderedColumns => _columns.AsReadOnly();

    public ReorderColumnsWindow(IEnumerable<string> columns)
    {
        _columns = columns.ToList();

        Title = "Reorder Columns";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DaTT.App/Assets/IconDaTT.ico")));
        Width = 420;
        MinHeight = 200;
        Height = Math.Min(560, Math.Max(220, _columns.Count * (ItemHeight + ItemSpacing) + 150));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        _itemsPanel = new StackPanel { Spacing = ItemSpacing, Margin = new Thickness(0, 4, 0, 8) };
        RebuildItemControls();

        var hint = new TextBlock
        {
            Text = "Drag items to reorder • changes take effect when you click Apply",
            Foreground = new SolidColorBrush(Color.Parse("#666666")),
            FontSize = 11,
            Margin = new Thickness(12, 10, 12, 6),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var scroll = new ScrollViewer
        {
            Content = _itemsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        var applyBtn = new Button { Content = "Apply", Width = 80 };
        applyBtn.Classes.Add("toolbar-btn");
        applyBtn.Classes.Add("primary");
        applyBtn.Click += (_, _) => { Confirmed = true; Close(); };

        var cancelBtn = new Button { Content = "Cancel", Width = 80 };
        cancelBtn.Classes.Add("toolbar-btn");
        cancelBtn.Click += (_, _) => Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(12, 8),
            Children = { cancelBtn, applyBtn }
        };

        var root = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(hint);
        root.Children.Add(buttonRow);
        root.Children.Add(scroll);

        Content = root;
    }

    // ── Item building ──────────────────────────────────────────────────────

    private void RebuildItemControls()
    {
        _itemsPanel.Children.Clear();
        _itemBorders.Clear();

        for (int i = 0; i < _columns.Count; i++)
        {
            var border = CreateItemBorder(_columns[i], i);
            _itemBorders.Add(border);
            _itemsPanel.Children.Add(border);
        }
    }

    private Border CreateItemBorder(string columnName, int index)
    {
        var dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.Parse("#555555")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
        };

        var label = new TextBlock
        {
            Text = columnName,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            VerticalAlignment = VerticalAlignment.Center
        };

        var badge = new TextBlock
        {
            Text = $"{index + 1}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#555555")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 2, 0)
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(dragHandle, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(badge, 2);
        row.Children.Add(dragHandle);
        row.Children.Add(label);
        row.Children.Add(badge);

        var border = new Border
        {
            Tag = columnName,
            Height = ItemHeight,
            Background = new SolidColorBrush(Color.Parse("#2D2D30")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3E3E42")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0),
            Margin = new Thickness(10, 0),
            Child = row,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
        };

        border.PointerPressed += OnItemPointerPressed;
        return border;
    }

    // ── Drag logic ─────────────────────────────────────────────────────────

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border border) return;

        _dragFromIndex = _itemBorders.IndexOf(border);
        SetDraggingStyle(border, true);

        // Use window-level tunnel so moves are captured even when pointer leaves the item
        this.AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
        this.AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel);

        e.Handled = true;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFromIndex < 0) return;

        var pos = e.GetPosition(_itemsPanel);
        var toIndex = HitTestIndex(pos.Y);

        if (toIndex != _dragFromIndex)
        {
            var dragging = _itemBorders[_dragFromIndex];
            _itemBorders.RemoveAt(_dragFromIndex);
            _itemBorders.Insert(toIndex, dragging);
            _itemsPanel.Children.RemoveAt(_dragFromIndex);
            _itemsPanel.Children.Insert(toIndex, dragging);
            _dragFromIndex = toIndex;
            UpdateBadges();
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragFromIndex >= 0 && _dragFromIndex < _itemBorders.Count)
            SetDraggingStyle(_itemBorders[_dragFromIndex], false);

        _dragFromIndex = -1;

        // Persist the new order from _itemBorders
        _columns.Clear();
        foreach (var b in _itemBorders)
            _columns.Add((string)b.Tag!);

        this.RemoveHandler(PointerMovedEvent, OnWindowPointerMoved);
        this.RemoveHandler(PointerReleasedEvent, OnWindowPointerReleased);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private int HitTestIndex(double y)
    {
        var idx = (int)(y / (ItemHeight + ItemSpacing));
        return Math.Clamp(idx, 0, Math.Max(0, _itemBorders.Count - 1));
    }

    private static void SetDraggingStyle(Border border, bool isDragging)
    {
        border.Background = new SolidColorBrush(
            Color.Parse(isDragging ? "#0E639C" : "#2D2D30"));
        border.Opacity = isDragging ? 0.85 : 1.0;
    }

    private void UpdateBadges()
    {
        for (int i = 0; i < _itemBorders.Count; i++)
        {
            if (_itemBorders[i].Child is Grid g && g.Children.Count >= 3 && g.Children[2] is TextBlock badge)
                badge.Text = $"{i + 1}";
        }
    }
}
