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
        _vm.ClipboardRequested += OnClipboardRequested;
        _vm.PickExportPath = PickExportPathAsync;
        _vm.PickImportPath = PickImportPathAsync;

        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is not null && !ReferenceEquals(grid, _wiredGrid))
        {
            grid.DoubleTapped += OnGridDoubleTapped;
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

        grid.Columns.Clear();
        for (int i = 0; i < _vm.ColumnNames.Count; i++)
        {
            int idx = i;
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = _vm.ColumnNames[i],
                CellTemplate = BuildCellTemplate(idx),
                CellEditingTemplate = BuildCellEditingTemplate(idx),
                IsReadOnly = false,
                MaxWidth = 300
            });
        }
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
        var menuSetNull   = this.FindControl<MenuItem>("MenuSetNull");

        AppLog.Info($"ContextMenu items � Edit:{menuEdit is not null} Dup:{menuDuplicate is not null} Del:{menuDelete is not null}");

        if (grid.ContextMenu is { } cm)
        {
            cm.Opening += (_, _) =>
            {
                bool has = _vm?.SelectedRow is not null;
                if (menuEdit      is not null) menuEdit.IsEnabled      = has;
                if (menuDuplicate is not null) menuDuplicate.IsEnabled = has;
                if (menuDelete    is not null) menuDelete.IsEnabled    = has;
                if (menuCopy      is not null) menuCopy.IsEnabled      = has;
                if (menuSetNull   is not null) menuSetNull.IsEnabled   = has;
            };
        }

        if (menuEdit      is not null) menuEdit.Click      += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.EditRowCommand.Execute(r); };
        if (menuDuplicate is not null) menuDuplicate.Click += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.DuplicateRowCommand.Execute(r); };
        if (menuDelete    is not null) menuDelete.Click    += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.DeleteRowCommand.Execute(r); };
        if (menuCopy      is not null) menuCopy.Click      += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.CopyRowCommand.Execute(r); };
        if (menuSetNull   is not null) menuSetNull.Click   += (_, _) => { if (_vm?.SelectedRow is { } r) _vm.SetNullCommand.Execute(r); };
    }

    // -- Events ----------------------------------------------------------------

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

    private async void OnOpenDesignPanelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var owner = GetOwnerWindow();
        if (owner is null)
            return;

        var dialog = new TableDesignWindow(_vm);
        await dialog.ShowDialog(owner);
    }
}
