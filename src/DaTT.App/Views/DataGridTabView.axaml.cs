using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DaTT.App.Infrastructure;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class DataGridTabView : UserControl
{
    private DataGridTabViewModel? _vm;
    private DataGrid? _wiredGrid;
    private readonly Dictionary<string, TextBlock> _sortIndicators = [];
    private readonly Dictionary<string, DataGridTemplateColumn> _columnMap = [];

    public DataGridTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ColumnNames.CollectionChanged -= OnColumnNamesChanged;
            _vm.ShowEditDialog    = null;
            _vm.ShowExpandDialog  = null;
            _vm.ShowCellEditDialog = null;
            _vm.ShowConfirmDialog = null;
            _vm.ShowSetValuesDialog = null;
            _vm.ClipboardRequested -= OnClipboardRequested;
            _vm.PickExportPath = null;
            _vm.PickImportPath = null;
        }

        _vm = DataContext as DataGridTabViewModel;
        if (_vm is null) return;

        _vm.ColumnNames.CollectionChanged += OnColumnNamesChanged;
        _vm.ShowEditDialog    = ShowEditDialogAsync;
        _vm.ShowExpandDialog  = ShowExpandDialogAsync;
        _vm.ShowCellEditDialog = ShowCellEditDialogAsync;
        _vm.ShowConfirmDialog = ShowConfirmDialogAsync;
        _vm.ShowSetValuesDialog = ShowSetValuesDialogAsync;
        _vm.ClipboardRequested += OnClipboardRequested;
        _vm.PickExportPath = PickExportPathAsync;
        _vm.PickImportPath = PickImportPathAsync;

        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is not null && !ReferenceEquals(grid, _wiredGrid))
        {
            grid.DoubleTapped += OnGridDoubleTapped;
            grid.Sorting += OnGridSorting;
            grid.SelectionChanged += OnGridSelectionChanged;
            grid.AddHandler(Button.ClickEvent, OnGridButtonClick, handledEventsToo: true);
            WireContextMenu(grid);
            _wiredGrid = grid;
        }

        RebuildColumns();
    }

    // -- Columns --------------------------------------------------------------

    private void OnColumnNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildColumns();

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is null || _vm is null) return;

        _sortIndicators.Clear();
        _columnMap.Clear();
        grid.Columns.Clear();

        for (int i = 0; i < _vm.ColumnNames.Count; i++)
        {
            int idx = i;
            var columnName = _vm.ColumnNames[i];
            var (header, indicator) = BuildSortableHeader(
                columnName,
                () => _ = DropColumnAsync(columnName),
                () => _ = SortAndRefreshAsync(columnName));

            _sortIndicators[columnName] = indicator;

            var col = new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = BuildCellTemplate(idx),
                CellEditingTemplate = BuildCellEditingTemplate(idx),
                IsReadOnly = false,
                MaxWidth = 300
            };
            _columnMap[columnName] = col;
            grid.Columns.Add(col);
        }

        // "+" column at the end for adding a new column
        var plusBtn = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0),
            Width = 24,
            Height = 22,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
        ToolTip.SetTip(plusBtn, "Add a column");
        plusBtn.PointerEntered += (_, _) => plusBtn.Background = new SolidColorBrush(Color.Parse("#1177BB"));
        plusBtn.PointerExited  += (_, _) => plusBtn.Background = new SolidColorBrush(Color.Parse("#007ACC"));
        plusBtn.PointerPressed += async (_, _) => await ShowAddColumnDialogAsync();

        // Wrap in a Grid so it stretches to fill the header cell and centers the button
        var plusHeaderWrapper = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { plusBtn }
        };
        Grid.SetRow(plusBtn, 0);
        plusBtn.HorizontalAlignment = HorizontalAlignment.Center;
        plusBtn.VerticalAlignment = VerticalAlignment.Center;

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = plusHeaderWrapper,
            CellTemplate = new FuncDataTemplate<GridRow>((_, _) => new Border()),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            Width = new DataGridLength(40)
        });

        ApplyColumnDisplayOrder();
        UpdateSortIndicators();
    }

    private static (Control Header, TextBlock Indicator) BuildSortableHeader(
        string columnName, Action onDropColumn, Action onSort)
    {
        var dropItem = new MenuItem { Header = $"Drop column '{columnName}'" };
        dropItem.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
        dropItem.Click += (_, _) => onDropColumn();

        var indicator = new TextBlock
        {
            Text = "⇅",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.4
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            ContextMenu = new ContextMenu { Items = { dropItem } },
            Children =
            {
                new TextBlock
                {
                    Text = columnName,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                indicator
            }
        };

        // Left-click triggers sort (right-click is handled by the ContextMenu)
        header.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                onSort();
        };

        return (header, indicator);
    }

    private async Task SortAndRefreshAsync(string columnName)
    {
        if (_vm is null) return;
        await _vm.SortByColumnAsync(columnName);
        UpdateSortIndicators();
    }

    private void UpdateSortIndicators()
    {
        if (_vm is null) return;
        foreach (var (colName, indicator) in _sortIndicators)
        {
            if (colName == _vm.SortColumn)
            {
                indicator.Text = _vm.SortAscending == true ? "↑" : "↓";
                indicator.Opacity = 1.0;
                indicator.Foreground = new SolidColorBrush(Color.Parse("#007ACC"));
            }
            else
            {
                indicator.Text = "⇅";
                indicator.Opacity = 0.4;
                indicator.Foreground = new SolidColorBrush(Color.Parse("#888888"));
            }
        }
    }

    private void ApplyColumnDisplayOrder()
    {
        if (_vm is null || _vm.ColumnDisplayOrder.Count == 0) return;

        var allNames = _vm.ColumnNames.ToList();
        var ordered = allNames
            .OrderBy(n => _vm.ColumnDisplayOrder.TryGetValue(n, out var idx) ? idx : int.MaxValue)
            .ThenBy(n => allNames.IndexOf(n))
            .ToList();

        for (int displayIdx = 0; displayIdx < ordered.Count; displayIdx++)
        {
            if (_columnMap.TryGetValue(ordered[displayIdx], out var col))
                col.DisplayIndex = displayIdx;
        }
    }

    private async Task ShowReorderColumnsDialogAsync()
    {
        if (_vm is null || GetOwnerWindow() is not { } owner) return;

        var dialog = new ReorderColumnsWindow(_vm.ColumnNames);
        await dialog.ShowDialog(owner);

        if (!dialog.Confirmed) return;

        var newOrder = dialog.OrderedColumns;
        _vm.ColumnDisplayOrder.Clear();
        for (int i = 0; i < newOrder.Count; i++)
            _vm.ColumnDisplayOrder[newOrder[i]] = i;

        ApplyColumnDisplayOrder();
    }

    private async Task DropColumnAsync(string columnName)
    {
        if (_vm is null) return;

        var confirmed = await ShowConfirmDialogAsync($"Drop column '{columnName}' from '{_vm.TableName}'? This cannot be undone.");
        if (!confirmed) return;

        var quotedTable  = $"\"{_vm.TableName.Replace("\"", "\"\"")}\"";
        var quotedColumn = $"\"{columnName.Replace("\"", "\"\"")}\"";
        var sql = $"ALTER TABLE {quotedTable} DROP COLUMN {quotedColumn};";

        await _vm.ExecuteDesignSqlAsync(sql);
    }

    private static FuncDataTemplate<GridRow> BuildCellTemplate(int columnIndex)
    {
        return new FuncDataTemplate<GridRow>((_, _) =>
        {
            var panel = new DockPanel();

            var button = new Button
            {
                Content = "\U0001F50D",
                FontSize = 11,
                Padding = new Thickness(2, 0),
                MinWidth = 0,
                MinHeight = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = columnIndex
            };
            button.Bind(Button.IsVisibleProperty, new Binding($"Cells[{columnIndex}].IsJson"));
            DockPanel.SetDock(button, Dock.Right);
            panel.Children.Add(button);

            var text = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(4, 0)
            };
            text.Bind(TextBlock.TextProperty, new Binding($"[{columnIndex}]"));
            panel.Children.Add(text);

            return panel;
        });
    }

    private static FuncDataTemplate<GridRow> BuildCellEditingTemplate(int columnIndex)
    {
        return new FuncDataTemplate<GridRow>((_, _) =>
        {
            var textBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(4, 2),
                BorderThickness = new Thickness(0)
            };
            textBox.Bind(TextBox.TextProperty, new Binding($"[{columnIndex}]") { Mode = BindingMode.TwoWay });
            return textBox;
        });
    }

    // -- Context menu ---------------------------------------------------------

    private void WireContextMenu(DataGrid grid)
    {
        // Items live in the UserControl namescope, not the ContextMenu's own scope
        var menuEdit      = this.FindControl<MenuItem>("MenuEditRow");
        var menuDuplicate = this.FindControl<MenuItem>("MenuDuplicateRow");
        var menuDelete    = this.FindControl<MenuItem>("MenuDeleteRow");
        var menuCopy      = this.FindControl<MenuItem>("MenuCopyRow");
        var menuSetValues = this.FindControl<MenuItem>("MenuSetValues");

        AppLog.Info($"ContextMenu items — Edit:{menuEdit is not null} Dup:{menuDuplicate is not null} Del:{menuDelete is not null}");

        if (grid.ContextMenu is { } cm)
        {
            cm.Opening += (_, _) =>
            {
                bool has = _vm?.SelectedRow is not null;
                bool hasMulti = _vm?.SelectedRowsList.Count > 0;
                if (menuEdit      is not null) menuEdit.IsEnabled      = has;
                if (menuDuplicate is not null) menuDuplicate.IsEnabled = has;
                if (menuDelete    is not null) menuDelete.IsEnabled    = has;
                if (menuCopy      is not null) menuCopy.IsEnabled      = has;
                if (menuSetValues is not null) menuSetValues.IsEnabled = hasMulti;
            };
        }

        if (menuEdit      is not null) menuEdit.Click      += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.EditRowCommand.Execute(r); };
        if (menuDuplicate is not null) menuDuplicate.Click += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.DuplicateRowCommand.Execute(r); };
        if (menuDelete    is not null) menuDelete.Click    += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.DeleteRowCommand.Execute(r); };
        if (menuCopy      is not null) menuCopy.Click      += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.CopyRowCommand.Execute(r); };
        if (menuSetValues is not null) menuSetValues.Click += (_, _) => { if (_vm is not null) _vm.SetValuesCommand.Execute(null); };
    }

    // -- Events ----------------------------------------------------------------

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || sender is not DataGrid grid) return;
        _vm.SelectedRowsList.Clear();
        foreach (var item in grid.SelectedItems)
        {
            if (item is GridRow row)
                _vm.SelectedRowsList.Add(row);
        }
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true; // Cancel Avalonia's client-side sort

        string? columnName = e.Column.Header switch
        {
            string s => s,
            TextBlock tb => tb.Text,
            StackPanel sp => (sp.Children.OfType<TextBlock>().FirstOrDefault())?.Text,
            _ => null
        };
        if (columnName is null) return;

        _ = _vm?.SortByColumnAsync(columnName);
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (e.Source is not Avalonia.Visual visual) return;

        var cell = visual.FindAncestorOfType<DataGridCell>();
        if (cell is null) return;

        var row = visual.FindAncestorOfType<DataGridRow>()?.DataContext as GridRow;
        if (row is null) return;

        var columnIndex = grid.CurrentColumn?.DisplayIndex ?? -1;
        if (columnIndex < 0 || columnIndex >= row.Cells.Length) return;

        if (row.Cells[columnIndex].IsJson)
            _vm?.EditCellCommand.Execute(new CellEditRequest(row, columnIndex));
        else
            _vm?.EnableInlineEditCommand.Execute(new CellEditRequest(row, columnIndex));
    }

    private void OnGridButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button btn || btn.Tag is not int columnIndex) return;

        var row = btn.FindAncestorOfType<DataGridRow>()?.DataContext as GridRow;
        if (row is null || _vm is null) return;

        _vm.EditCellCommand.Execute(new CellEditRequest(row, columnIndex));
    }

    // -- Dialogs ---------------------------------------------------------------

    private async Task ShowEditDialogAsync(EditRowViewModel vm)
    {
        if (GetOwnerWindow() is not { } owner) return;
        await new EditRowWindow { DataContext = vm }.ShowDialog(owner);
    }

    private async Task ShowExpandDialogAsync(CellExpandViewModel vm)
    {
        if (GetOwnerWindow() is not { } owner) return;
        await new CellExpandWindow { DataContext = vm }.ShowDialog(owner);
    }

    private async Task ShowCellEditDialogAsync(CellEditViewModel vm)
    {
        if (GetOwnerWindow() is not { } owner) return;
        await new CellEditWindow { DataContext = vm }.ShowDialog(owner);
    }

    private async Task<bool> ShowConfirmDialogAsync(string message)
    {
        if (GetOwnerWindow() is not { } owner) return false;
        return await ConfirmDialog.ShowAsync(owner, message);
    }

    private async Task ShowSetValuesDialogAsync(SetValuesViewModel vm)
    {
        if (GetOwnerWindow() is not { } owner) return;
        await new SetValuesWindow { DataContext = vm }.ShowDialog(owner);
    }

    private async Task ShowAddColumnDialogAsync()
    {
        if (_vm is null || GetOwnerWindow() is not { } owner) return;
        await new AddColumnWindow(_vm).ShowDialog(owner);
    }

    private void OnClipboardRequested(string text)
        => TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);

    private Window? GetOwnerWindow()
        => this.GetVisualRoot() as Window;

    private async Task<string?> PickExportPathAsync(string format)
    {
        var owner = GetOwnerWindow();
        if (owner?.StorageProvider is not { } storageProvider)
            return null;

        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        var extension = normalized switch
        {
            "json" => "json",
            "sql" => "sql",
            "xlsx" => "xlsx",
            _ => "csv"
        };

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export table data",
            SuggestedFileName = $"{_vm?.TableName ?? "export"}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType($"{extension.ToUpperInvariant()} files")
                {
                    Patterns = [$"*.{extension}"]
                }
            ]
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickImportPathAsync()
    {
        var owner = GetOwnerWindow();
        if (owner?.StorageProvider is not { } storageProvider)
            return null;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import SQL script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQL files")
                {
                    Patterns = ["*.sql", "*.txt"]
                }
            ]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void OnReorderColumnsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await ShowReorderColumnsDialogAsync();
}
