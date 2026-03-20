using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

internal sealed class TableDesignWindow : Window
{
    private readonly DataGridTabViewModel _viewModel;

    private readonly TextBox _renameTableBox = new();
    private readonly TextBox _addColumnNameBox = new();
    private readonly TextBox _addColumnTypeBox = new() { Text = "varchar(255)" };
    private readonly CheckBox _addColumnNullableCheck = new() { Content = "Nullable", IsChecked = true };
    private readonly TextBox _dropColumnNameBox = new();

    private readonly TextBox _createIndexNameBox = new();
    private readonly TextBox _createIndexColumnsBox = new();
    private readonly CheckBox _createIndexUniqueCheck = new() { Content = "Unique", IsChecked = false };
    private readonly TextBox _dropIndexNameBox = new();

    private readonly TextBox _sqlPreviewBox = new()
    {
        AcceptsReturn = true,
        IsReadOnly = true,
        Height = 150
    };

    private readonly TextBlock _statusText = new()
    {
        Foreground = Avalonia.Media.Brushes.Gray,
        Text = "Generate SQL from the tabs and click Execute SQL."
    };

    public TableDesignWindow(DataGridTabViewModel viewModel)
    {
        _viewModel = viewModel;

        Title = $"Design Table - {_viewModel.TableName}";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DaTT.App/Assets/IconDaTT.ico")));
        Width = 860;
        Height = 620;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var tabControl = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Columns", Content = BuildColumnsPanel() },
                new TabItem { Header = "Indexes", Content = BuildIndexesPanel() }
            }
        };

        var executeButton = new Button { Content = "Execute SQL", Width = 120, HorizontalAlignment = HorizontalAlignment.Right };
        executeButton.Click += async (_, _) =>
        {
            var sql = _sqlPreviewBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sql))
            {
                _statusText.Text = "No SQL generated yet.";
                return;
            }

            var ok = await _viewModel.ExecuteDesignSqlAsync(sql);
            _statusText.Text = ok ? "SQL executed successfully." : $"Execution failed: {_viewModel.ErrorMessage}";
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(12)
        };

        root.Children.Add(tabControl);

        Grid.SetRow(_sqlPreviewBox, 1);
        _sqlPreviewBox.Margin = new Thickness(0, 10, 0, 8);
        root.Children.Add(_sqlPreviewBox);

        Grid.SetRow(_statusText, 2);
        _statusText.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(_statusText);

        Grid.SetRow(executeButton, 3);
        root.Children.Add(executeButton);

        return root;
    }

    private Control BuildColumnsPanel()
    {
        var renameButton = new Button { Content = "Generate Rename SQL", Width = 170 };
        renameButton.Click += (_, _) =>
        {
            var newName = _renameTableBox.Text?.Trim() ?? string.Empty;
            if (!IsSafeIdentifier(newName))
            {
                _statusText.Text = "Invalid table name for rename.";
                return;
            }

            _sqlPreviewBox.Text = $"ALTER TABLE {QuoteIdentifier(_viewModel.TableName)} RENAME TO {QuoteIdentifier(newName)};";
            _statusText.Text = "Rename SQL generated.";
        };

        var addColumnButton = new Button { Content = "Generate Add Column SQL", Width = 190 };
        addColumnButton.Click += (_, _) =>
        {
            var col = _addColumnNameBox.Text?.Trim() ?? string.Empty;
            var type = _addColumnTypeBox.Text?.Trim() ?? string.Empty;
            var nullable = _addColumnNullableCheck.IsChecked ?? true;

            if (!IsSafeIdentifier(col) || string.IsNullOrWhiteSpace(type))
            {
                _statusText.Text = "Invalid column definition.";
                return;
            }

            var nullClause = nullable ? string.Empty : " NOT NULL";
            _sqlPreviewBox.Text = $"ALTER TABLE {QuoteIdentifier(_viewModel.TableName)} ADD COLUMN {QuoteIdentifier(col)} {type}{nullClause};";
            _statusText.Text = "Add column SQL generated.";
        };

        var dropColumnButton = new Button { Content = "Generate Drop Column SQL", Width = 190 };
        dropColumnButton.Click += (_, _) =>
        {
            var col = _dropColumnNameBox.Text?.Trim() ?? string.Empty;
            if (!IsSafeIdentifier(col))
            {
                _statusText.Text = "Invalid column name to drop.";
                return;
            }

            _sqlPreviewBox.Text = $"ALTER TABLE {QuoteIdentifier(_viewModel.TableName)} DROP COLUMN {QuoteIdentifier(col)};";
            _statusText.Text = "Drop column SQL generated.";
        };

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildField("Rename table to", _renameTableBox),
                renameButton,
                new Separator(),
                BuildField("Add column name", _addColumnNameBox),
                BuildField("Add column type", _addColumnTypeBox),
                _addColumnNullableCheck,
                addColumnButton,
                new Separator(),
                BuildField("Drop column name", _dropColumnNameBox),
                dropColumnButton
            }
        };
    }

    private Control BuildIndexesPanel()
    {
        var createIndexButton = new Button { Content = "Generate Create Index SQL", Width = 200 };
        createIndexButton.Click += (_, _) =>
        {
            var idxName = _createIndexNameBox.Text?.Trim() ?? string.Empty;
            var cols = (_createIndexColumnsBox.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var unique = _createIndexUniqueCheck.IsChecked ?? false;

            if (!IsSafeIdentifier(idxName) || cols.Count == 0 || cols.Any(c => !IsSafeIdentifier(c)))
            {
                _statusText.Text = "Invalid index definition.";
                return;
            }

            var uniquePart = unique ? "UNIQUE " : string.Empty;
            var colSql = string.Join(", ", cols.Select(QuoteIdentifier));
            _sqlPreviewBox.Text = $"CREATE {uniquePart}INDEX {QuoteIdentifier(idxName)} ON {QuoteIdentifier(_viewModel.TableName)} ({colSql});";
            _statusText.Text = "Create index SQL generated.";
        };

        var dropIndexButton = new Button { Content = "Generate Drop Index SQL", Width = 190 };
        dropIndexButton.Click += (_, _) =>
        {
            var idxName = _dropIndexNameBox.Text?.Trim() ?? string.Empty;
            if (!IsSafeIdentifier(idxName))
            {
                _statusText.Text = "Invalid index name to drop.";
                return;
            }

            var dropSql = IsMySqlLike(_viewModel.EngineName)
                ? $"DROP INDEX {QuoteIdentifier(idxName)} ON {QuoteIdentifier(_viewModel.TableName)};"
                : $"DROP INDEX {QuoteIdentifier(idxName)};";

            _sqlPreviewBox.Text = dropSql;
            _statusText.Text = "Drop index SQL generated.";
        };

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildField("Create index name", _createIndexNameBox),
                BuildField("Create index columns (comma separated)", _createIndexColumnsBox),
                _createIndexUniqueCheck,
                createIndexButton,
                new Separator(),
                BuildField("Drop index name", _dropIndexNameBox),
                dropIndexButton
            }
        };
    }

    private static Control BuildField(string label, Control field)
        => new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label },
                field
            }
        };

    private static bool IsSafeIdentifier(string identifier)
        => Regex.IsMatch(identifier, "^[A-Za-z_][A-Za-z0-9_]*$");

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static bool IsMySqlLike(string engineName)
        => engineName.Contains("mysql", StringComparison.OrdinalIgnoreCase)
           || engineName.Contains("maria", StringComparison.OrdinalIgnoreCase);
}
