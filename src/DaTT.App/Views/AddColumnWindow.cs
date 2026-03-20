using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

internal sealed class AddColumnWindow : Window
{
    private readonly DataGridTabViewModel _viewModel;

    private readonly TextBox _columnNameBox = new() { Watermark = "column_name", Width = 200 };
    private readonly ComboBox _columnTypeCombo = new() { Width = 200 };
    private readonly TextBox _sizeBox = new() { Watermark = "e.g. 255", Width = 100 };
    private readonly TextBox _precisionBox = new() { Watermark = "e.g. 10,2", Width = 100 };
    private readonly CheckBox _nullableCheck = new() { Content = "Nullable", IsChecked = true };
    private readonly TextBox _defaultValueBox = new() { Watermark = "(optional)", Width = 200 };
    private readonly TextBox _sqlPreviewBox = new() { AcceptsReturn = false, IsReadOnly = true, Height = 40, FontFamily = new FontFamily("Consolas,Courier New,monospace"), FontSize = 11 };
    private readonly TextBlock _statusText = new() { Foreground = Brushes.Gray, Text = "Fill in the fields and click Add Column." };

    public AddColumnWindow(DataGridTabViewModel viewModel)
    {
        _viewModel = viewModel;

        Title = $"Add Column — {_viewModel.TableName}";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DaTT.App/Assets/IconDaTT.ico")));
        Width = 480;
        Height = 420;
        MinWidth = 400;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var types = ColumnTypeRegistry.GetTypes(_viewModel.EngineName);
        _columnTypeCombo.ItemsSource = types;
        _columnTypeCombo.SelectedIndex = 0;
        _columnTypeCombo.SelectionChanged += OnTypeSelectionChanged;

        _sizeBox.TextChanged += (_, _) => RegenerateSql();
        _precisionBox.TextChanged += (_, _) => RegenerateSql();
        _columnNameBox.TextChanged += (_, _) => RegenerateSql();
        _nullableCheck.IsCheckedChanged += (_, _) => RegenerateSql();
        _defaultValueBox.TextChanged += (_, _) => RegenerateSql();

        Content = BuildLayout();
        UpdateSizeVisibility();
        RegenerateSql();
    }

    private Control BuildLayout()
    {
        var addButton = new Button { Content = "Add Column", Width = 130, HorizontalAlignment = HorizontalAlignment.Right };
        addButton.Classes.Add("primary");
        addButton.Click += async (_, _) => await OnAddColumnClickedAsync();

        var cancelButton = new Button { Content = "Cancel", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        cancelButton.Classes.Add("ghost");
        cancelButton.Click += (_, _) => Close();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { addButton, cancelButton }
        };

        var root = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                BuildRow("Column name", _columnNameBox),
                BuildRow("Column type", _columnTypeCombo),
                BuildRow("Length", _sizeBox),
                BuildRow("Precision, Scale", _precisionBox),
                _nullableCheck,
                BuildRow("Default value", _defaultValueBox),
                new TextBlock { Text = "SQL Preview:", FontSize = 11, Foreground = Brushes.Gray },
                _sqlPreviewBox,
                _statusText,
                buttonPanel
            }
        };

        return new ScrollViewer { Content = root };
    }

    private static Control BuildRow(string label, Control control)
        => new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*"),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                AddColumn(control, 1)
            }
        };

    private static Control AddColumn(Control c, int col)
    {
        Grid.SetColumn(c, col);
        return c;
    }

    private void OnTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSizeVisibility();
        RegenerateSql();
    }

    private void UpdateSizeVisibility()
    {
        var typeName = _columnTypeCombo.SelectedItem as string ?? string.Empty;
        var needsLen = ColumnTypeRegistry.NeedsLength(typeName);
        var needsPrec = ColumnTypeRegistry.NeedsPrecision(typeName);

        _sizeBox.IsVisible = needsLen;
        _precisionBox.IsVisible = needsPrec;

        var sizeRow = _sizeBox.Parent as Grid;
        if (sizeRow is not null) sizeRow.IsVisible = needsLen;

        var precRow = _precisionBox.Parent as Grid;
        if (precRow is not null) precRow.IsVisible = needsPrec;
    }

    private void RegenerateSql()
    {
        var col = _columnNameBox.Text?.Trim() ?? string.Empty;
        var typeName = _columnTypeCombo.SelectedItem as string ?? string.Empty;
        var nullable = _nullableCheck.IsChecked ?? true;
        var defaultVal = _defaultValueBox.Text?.Trim() ?? string.Empty;

        if (!IsSafeIdentifier(col) || string.IsNullOrWhiteSpace(typeName))
        {
            _sqlPreviewBox.Text = string.Empty;
            return;
        }

        var fullType = BuildFullType(typeName);
        var nullClause = nullable ? string.Empty : " NOT NULL";
        var defaultClause = string.IsNullOrWhiteSpace(defaultVal) ? string.Empty : $" DEFAULT {defaultVal}";

        _sqlPreviewBox.Text = $"ALTER TABLE {QuoteIdentifier(_viewModel.TableName)} ADD COLUMN {QuoteIdentifier(col)} {fullType}{nullClause}{defaultClause};";
    }

    private string BuildFullType(string typeName)
    {
        if (ColumnTypeRegistry.NeedsLength(typeName))
        {
            var size = _sizeBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(size) && int.TryParse(size, out var len) && len > 0)
                return $"{typeName}({len})";
            return $"{typeName}(255)";
        }

        if (ColumnTypeRegistry.NeedsPrecision(typeName))
        {
            var prec = _precisionBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(prec))
                return $"{typeName}({prec})";
            return typeName;
        }

        return typeName;
    }

    private async Task OnAddColumnClickedAsync()
    {
        var sql = _sqlPreviewBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sql))
        {
            _statusText.Text = "Fix the column definition first.";
            _statusText.Foreground = Brushes.OrangeRed;
            return;
        }

        var ok = await _viewModel.ExecuteDesignSqlAsync(sql);
        if (ok)
        {
            Close();
        }
        else
        {
            _statusText.Text = $"Failed: {_viewModel.ErrorMessage}";
            _statusText.Foreground = Brushes.OrangeRed;
        }
    }

    private static bool IsSafeIdentifier(string id)
        => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^[A-Za-z_][A-Za-z0-9_]*$");

    private static string QuoteIdentifier(string id)
        => $"\"{id.Replace("\"", "\"\"")}\"";
}
