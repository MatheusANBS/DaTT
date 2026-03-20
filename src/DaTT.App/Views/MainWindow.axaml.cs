using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ConnectionManager.LoadConnectionsAsync();
            vm.OpenConnectionManagerCommand.Execute(null);
            vm.NotifyAppReady();
        }

        var tabsControl = this.FindControl<TabControl>("TabsControl");
        if (tabsControl is not null)
            tabsControl.AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Bubble);
    }

    private async void OnUpdateButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.PendingRelease is null) return;
        var win = new UpdateWindow(vm.PendingRelease) { };
        await win.ShowDialog(this);
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var tabItem = (e.Source as Avalonia.Visual)?.FindAncestorOfType<TabItem>();
        if (tabItem?.DataContext is not TabViewModel clickedTab) return;

        var cm = new ContextMenu();
        cm.Items.Add(new MenuItem { Header = "Close", Command = vm.CloseTabCommand, CommandParameter = clickedTab });
        cm.Items.Add(new MenuItem { Header = "Close Others", Command = vm.CloseOtherTabsCommand, CommandParameter = clickedTab });
        cm.Items.Add(new Separator());
        cm.Items.Add(new MenuItem { Header = "Close All", Command = vm.CloseAllTabsCommand });

        cm.Open(tabItem);
        e.Handled = true;
    }

    private async void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var node = ResolveTreeNode(e.Source as Avalonia.Visual);
        if (node is null) return;

        if (node.NodeType is TreeNodeType.Schema or TreeNodeType.Folder or TreeNodeType.Connection)
        {
            if (node.NodeType == TreeNodeType.Schema)
                await vm.ObjectExplorer.ExpandSchemaNodeAsync(node);

            node.IsExpanded = !node.IsExpanded;
        }
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var node = ResolveTreeNode(e.Source as Avalonia.Visual);
        if (node is null) return;

        if (node.NodeType is TreeNodeType.Table or TreeNodeType.View)
            vm.OpenTableCommand.Execute(node);
    }

    // Walk up from the tapped element to find the TreeNodeViewModel.
    // Returns null if the tap landed on the native expand-toggle button.
    private static TreeNodeViewModel? ResolveTreeNode(Avalonia.Visual? source)
    {
        for (var c = source as Control; c is not null; c = c.Parent as Control)
        {
            if (c is Avalonia.Controls.Primitives.ToggleButton) return null;
            if (c.DataContext is TreeNodeViewModel node) return node;
        }
        return null;
    }

    private void OnTreeContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        var isTable = node?.NodeType == TreeNodeType.Table;

        SetMenuEnabled("MenuOpenTable", isTable);
        SetMenuEnabled("MenuSelectTemplate", isTable);
        SetMenuEnabled("MenuCountTemplate", isTable);
        SetMenuEnabled("MenuShowSource", isTable);
        SetMenuEnabled("MenuCopyName", node is not null);
        SetMenuEnabled("MenuTruncate", isTable);
        SetMenuEnabled("MenuDrop", isTable);
        SetMenuEnabled("MenuDumpTable", isTable);
        SetMenuEnabled("MenuCreateTable", vm.IsConnected);

        var isSchema = node?.NodeType == TreeNodeType.Schema;
        var isTablesFolder = node?.NodeType == TreeNodeType.Folder && node.Label == "Tables";
        SetMenuEnabled("MenuDumpSchema", isSchema || isTablesFolder);
    }

    private void SetMenuEnabled(string menuName, bool enabled)
    {
        var menu = this.FindControl<MenuItem>(menuName);
        if (menu is not null)
            menu.IsEnabled = enabled;
    }

    private void OnOpenTableClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node?.NodeType != TreeNodeType.Table)
            return;

        vm.OpenTableCommand.Execute(node);
    }

    private void OnSelectTemplateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node is null)
            return;

        vm.OpenSelectTemplate(node);
    }

    private void OnCountTemplateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node is null)
            return;

        vm.OpenCountTemplate(node);
    }

    private async void OnShowSourceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node is null)
            return;

        await vm.ShowSourceAsync(node);
    }

    private async void OnCopyNameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node is null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(node.Label);
    }

    private async void OnTruncateTableClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node?.NodeType != TreeNodeType.Table)
            return;

        var confirmed = await ConfirmDialog.ShowAsync(this, $"Truncate table '{node.Label}'? This action removes all rows.");
        if (!confirmed)
            return;

        await vm.TruncateTableAsync(node);
    }

    private async void OnDropTableClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node?.NodeType != TreeNodeType.Table)
            return;

        var confirmed = await ConfirmDialog.ShowAsync(this, $"Drop table '{node.Label}'? This action cannot be undone.");
        if (!confirmed)
            return;

        await vm.DropTableAsync(node);
    }

    private async void OnDumpTableClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node?.NodeType != TreeNodeType.Table) return;

        var format = (sender as MenuItem)?.Tag?.ToString() ?? "csv";
        var path = await PickDumpPathAsync(node.Label, format);
        if (string.IsNullOrWhiteSpace(path)) return;

        await vm.DumpTableAsync(node.Label, format, path);
    }

    private async Task<string?> PickDumpPathAsync(string tableName, string format)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null) return null;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Dump {tableName}",
            SuggestedFileName = $"{tableName}.{format}",
            DefaultExtension = format,
            FileTypeChoices =
            [
                new FilePickerFileType($"{format.ToUpperInvariant()} files")
                {
                    Patterns = [$"*.{format}"]
                }
            ]
        });

        return file?.TryGetLocalPath();
    }

    private async void OnCreateTableClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsConnected) return;

        var createVm = new CreateTableViewModel(vm.ObjectExplorer.ActiveProvider?.EngineName ?? "");
        var dialog = new CreateTableWindow { DataContext = createVm };
        await dialog.ShowDialog(this);

        if (!createVm.Confirmed || string.IsNullOrWhiteSpace(createVm.GeneratedSql))
            return;

        await vm.CreateTableAsync(createVm.GeneratedSql);
    }

    private async void OnDumpSchemaClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var node = vm.ObjectExplorer.SelectedNode;
        if (node is null) return;

        var mode = (sender as MenuItem)?.Tag?.ToString() ?? "all";

        var tableNames = CollectTableNames(node);
        if (tableNames.Count == 0) return;

        var folder = await PickFolderAsync($"Dump {node.Label}");
        if (string.IsNullOrWhiteSpace(folder)) return;

        await vm.DumpSchemaAsync(tableNames, mode, folder);
    }

    private static List<string> CollectTableNames(TreeNodeViewModel node)
    {
        var tables = new List<string>();

        if (node.NodeType == TreeNodeType.Table)
        {
            tables.Add(node.Label);
            return tables;
        }

        foreach (var child in node.Children)
        {
            if (child.NodeType == TreeNodeType.Table)
                tables.Add(child.Label);
            else if (child.NodeType == TreeNodeType.Folder || child.NodeType == TreeNodeType.Schema)
                tables.AddRange(CollectTableNames(child));
        }

        return tables;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null) return null;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
