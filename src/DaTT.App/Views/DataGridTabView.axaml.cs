using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using DaTT.Core.Models;
using Material.Icons;
using Material.Icons.Avalonia;

namespace DaTT.App.Views;

public partial class DataGridTabView : UserControl
{
    private DataGridTabViewModel? _vm;
    private DataGrid? _wiredGrid;
    private readonly Dictionary<string, MaterialIcon> _sortIndicators = [];
    private readonly Dictionary<string, Button> _filterButtons = [];
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
            _vm.PropertyChanged -= OnVmPropertyChanged;
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
        _vm.PropertyChanged += OnVmPropertyChanged;
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
        _filterButtons.Clear();
        _columnMap.Clear();
        grid.Columns.Clear();

        for (int i = 0; i < _vm.ColumnNames.Count; i++)
        {
            int idx = i;
            var columnName = _vm.ColumnNames[i];
            var meta = _vm.ColumnInfos.FirstOrDefault(c => c.Name == columnName);
            var (header, indicator) = BuildSortableHeader(
                columnName,
                meta,
                () => _ = DropColumnAsync(columnName),
                () => _ = SortAndRefreshAsync(columnName));

            _sortIndicators[columnName] = indicator;

            var col = new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = BuildCellTemplate(idx),
                CellEditingTemplate = BuildCellEditingTemplate(idx, meta),
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
        UpdateFilterIndicators();
    }

    // Remove 'static' — now needs access to _filterButtons and _vm
    private (Control Header, MaterialIcon Indicator) BuildSortableHeader(
        string columnName, ColumnMeta? meta, Action onDropColumn, Action onSort)
    {
        var dropItem = new MenuItem { Header = $"Drop column '{columnName}'" };
        dropItem.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
        dropItem.Click += (_, _) => onDropColumn();

        var indicator = new MaterialIcon
        {
            Kind = MaterialIconKind.ArrowUpDown,
            Width = 14,
            Height = 14,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.4
        };

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
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

        var inner = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { topRow }
        };

        if (meta is not null)
        {
            var typeText = meta.SimpleType ?? meta.DataType;
            var infoRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = typeText,
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.Parse("#808080")),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontStyle = Avalonia.Media.FontStyle.Italic
                    }
                }
            };

            if (!meta.IsNullable)
            {
                infoRow.Children.Add(new TextBlock
                {
                    Text = "*",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#F44747")),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            inner.Children.Add(infoRow);
        }

        // ── Per-column filter flyout ──────────────────────────────────────
        var filterInput = new TextBox
        {
            Watermark = "Filter value...",
            Width = 190,
            FontSize = 12
        };

        var flyoutApplyBtn = new Button
        {
            Content = "Apply",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        flyoutApplyBtn.Classes.Add("toolbar-btn");
        flyoutApplyBtn.Classes.Add("primary");

        var flyoutClearBtn = new Button
        {
            Content = "Clear filter",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        flyoutClearBtn.Classes.Add("toolbar-btn");

        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Margin = new Thickness(4),
                Children = { filterInput, flyoutApplyBtn, flyoutClearBtn }
            }
        };

        flyout.Opening += (_, _) =>
        {
            filterInput.Text = (_vm?.FilterColumn == columnName) ? _vm.FilterValue : string.Empty;
            filterInput.Focus();
        };

        flyoutApplyBtn.Click += (_, _) =>
        {
            if (_vm is null) return;
            _vm.FilterColumn = columnName;
            _vm.FilterValue = filterInput.Text ?? string.Empty;
            flyout.Hide();
            _ = _vm.ApplyFilterCommand.ExecuteAsync(null);
            UpdateFilterIndicators();
        };

        flyoutClearBtn.Click += (_, _) =>
        {
            filterInput.Text = string.Empty;
            flyout.Hide();
            if (_vm is null) return;
            _ = _vm.ClearFilterCommand.ExecuteAsync(null);
            UpdateFilterIndicators();
        };

        filterInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                flyoutApplyBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        var filterBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.FilterOutline, Width = 14, Height = 14 },
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#555555")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand),
            Flyout = flyout,
            Opacity = 0.5
        };
        ToolTip.SetTip(filterBtn, $"Filter by {columnName}");
        // Stop event bubbling so clicking the filter btn doesn't also fire sort
        filterBtn.PointerReleased += (_, e) => e.Handled = true;
        _filterButtons[columnName] = filterBtn;

        // Grid: inner content left | filter button right
        var outerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        outerGrid.Children.Add(inner);
        outerGrid.Children.Add(filterBtn);
        Grid.SetColumn(inner, 0);
        Grid.SetColumn(filterBtn, 1);

        // Wrap in a full-width Border so the entire header cell area is hit-testable
        var header = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
            ContextMenu = new ContextMenu { Items = { dropItem } },
            Child = outerGrid
        };

        // Left-click triggers sort (filter button stops bubbling)
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
                indicator.Kind = _vm.SortAscending == true ? MaterialIconKind.ArrowUp : MaterialIconKind.ArrowDown;
                indicator.Opacity = 1.0;
                indicator.Foreground = new SolidColorBrush(Color.Parse("#007ACC"));
            }
            else
            {
                indicator.Kind = MaterialIconKind.ArrowUpDown;
                indicator.Opacity = 0.4;
                indicator.Foreground = new SolidColorBrush(Color.Parse("#888888"));
            }
        }
    }

    private void UpdateFilterIndicators()
    {
        if (_vm is null) return;
        var activeCol = _vm.FilterColumn;
        var hasFilter = !string.IsNullOrWhiteSpace(_vm.FilterValue);

        foreach (var (colName, btn) in _filterButtons)
        {
            var isActive = hasFilter && colName == activeCol;
            btn.Foreground = isActive
                ? new SolidColorBrush(Color.Parse("#007ACC"))
                : new SolidColorBrush(Color.Parse("#555555"));
            btn.Opacity = isActive ? 1.0 : 0.5;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DataGridTabViewModel.FilterColumn)
                           or nameof(DataGridTabViewModel.FilterValue))
            UpdateFilterIndicators();
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

    private static FuncDataTemplate<GridRow> BuildCellEditingTemplate(int columnIndex, ColumnMeta? meta)
    {
        if (meta?.IsDateTimeType == true)
            return BuildDateTimeEditTemplate(columnIndex, hasDate: true, hasTime: true);
        if (meta?.IsDateOnlyType == true)
            return BuildDateTimeEditTemplate(columnIndex, hasDate: true, hasTime: false);
        if (meta?.IsTimeOnlyType == true)
            return BuildDateTimeEditTemplate(columnIndex, hasDate: false, hasTime: true);

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

    private static FuncDataTemplate<GridRow> BuildDateTimeEditTemplate(int columnIndex, bool hasDate, bool hasTime)
    {
        return new FuncDataTemplate<GridRow>((_, _) =>
        {
            // Read-only text showing current value
            var valueBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(3, 1),
                BorderThickness = new Thickness(0),
                IsReadOnly = true,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 90
            };

            // Small calendar trigger button
            var triggerBtn = new Button
            {
                Content = new MaterialIcon { Kind = MaterialIconKind.Calendar, Width = 14, Height = 14 },
                Padding = new Thickness(5, 2),
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            // Pickers live inside the popup — no cell clipping
            CalendarDatePicker? datePicker = hasDate ? new CalendarDatePicker
            {
                Width = 200,
                FontSize = 12
            } : null;

            TimePicker? timePicker = hasTime ? new TimePicker
            {
                Width = 180,
                FontSize = 12
            } : null;

            var pickerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8
            };
            if (datePicker is not null) pickerStack.Children.Add(datePicker);
            if (timePicker is not null) pickerStack.Children.Add(timePicker);

            var popupBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                BorderBrush = new SolidColorBrush(Color.Parse("#007ACC")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12),
                BoxShadow = BoxShadows.Parse("0 4 16 2 #AA000000"),
                Child = pickerStack
            };

            var popup = new Popup
            {
                Child = popupBorder,
                PlacementMode = PlacementMode.Bottom,
                PlacementTarget = triggerBtn,
                IsLightDismissEnabled = false
            };

            triggerBtn.Click += (_, _) => popup.IsOpen = !popup.IsOpen;

            // Root: text display | button, popup floats in overlay above the grid
            var root = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBox, 0);
            Grid.SetColumn(triggerBtn, 1);
            root.Children.Add(valueBox);
            root.Children.Add(triggerBtn);
            root.Children.Add(popup);

            // Close popup when cell exits edit mode (e.g. user commits or navigates away)
            root.DetachedFromVisualTree += (_, _) => popup.IsOpen = false;

            bool initializing = false;

            // Auto-open the popup as soon as the editing template is shown
            root.AttachedToVisualTree += (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => popup.IsOpen = true,
                    Avalonia.Threading.DispatcherPriority.Background);

            root.DataContextChanged += (_, _) =>
            {
                if (root.DataContext is not GridRow row) return;
                initializing = true;
                try
                {
                    var str = row[columnIndex] ?? string.Empty;
                    valueBox.Text = str;
                    if (hasDate && hasTime)
                    {
                        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        {
                            datePicker!.SelectedDate = dt;
                            timePicker!.SelectedTime = dt.TimeOfDay;
                        }
                        else
                        {
                            datePicker!.SelectedDate = null;
                            timePicker!.SelectedTime = null;
                        }
                    }
                    else if (hasDate)
                    {
                        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                            datePicker!.SelectedDate = dt;
                        else
                            datePicker!.SelectedDate = null;
                    }
                    else
                    {
                        if (TimeSpan.TryParse(str, out var ts))
                            timePicker!.SelectedTime = ts;
                        else
                            timePicker!.SelectedTime = null;
                    }
                }
                finally { initializing = false; }
            };

            void Sync()
            {
                if (initializing || root.DataContext is not GridRow row) return;
                string newVal;
                if (hasDate && hasTime)
                {
                    var date = datePicker!.SelectedDate?.Date;
                    newVal = date.HasValue
                        ? date.Value.Add(timePicker!.SelectedTime ?? TimeSpan.Zero)
                            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : string.Empty;
                }
                else if (hasDate)
                {
                    newVal = datePicker!.SelectedDate.HasValue
                        ? datePicker.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : string.Empty;
                }
                else
                {
                    newVal = timePicker!.SelectedTime.HasValue
                        ? timePicker.SelectedTime.Value.ToString(@"hh\:mm\:ss")
                        : string.Empty;
                }
                row[columnIndex] = newVal;
                valueBox.Text = newVal;
            }

            if (datePicker is not null)
                datePicker.SelectedDateChanged += (_, _) => Sync();
            if (timePicker is not null)
                timePicker.PropertyChanged += (_, e) =>
                {
                    if (e.Property.Name == nameof(TimePicker.SelectedTime)) Sync();
                };

            return root;
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
        {
            _vm?.EditCellCommand.Execute(new CellEditRequest(row, columnIndex));
            return;
        }

        // Enable inline edit on first double-click, then go straight into cell edit
        if (_vm?.IsInlineEditMode == false)
            _vm.EnableInlineEditCommand.Execute(new CellEditRequest(row, columnIndex));

        // Immediately enter editing mode for the current cell
        grid.BeginEdit();
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
