using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.App.Infrastructure;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.App.ViewModels;

public partial class ObjectExplorerViewModel : ViewModelBase
{
    private readonly IProviderFactory _providerFactory;

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    private TreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string? _errorMessage;

    private IDatabaseProvider? _activeProvider;

    public IDatabaseProvider? ActiveProvider => _activeProvider;

    public ObjectExplorerViewModel(IProviderFactory providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public async Task LoadConnectionAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;

        if (_activeProvider is not null)
        {
            try
            {
                await _activeProvider.DisposeAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Ignoring provider dispose failure before reconnect: {ex.Message}");
            }
        }

        try
        {
            var provider = _providerFactory.CreateForConnectionString(config.ConnectionString);
            await provider.ConnectAsync(config.ConnectionString, cancellationToken);
            _activeProvider = provider;

            RootNodes.Clear();

            var connectionNode = new TreeNodeViewModel(config.Name, TreeNodeType.Connection);

            // Try to load schemas — if provider supports them, show schema tree
            var schemas = await _activeProvider.GetSchemasAsync(cancellationToken);

            if (schemas.Count > 1)
            {
                // Multi-schema DB (e.g. PostgreSQL) — show schema nodes
                foreach (var schemaName in schemas)
                {
                    var schemaNode = new TreeNodeViewModel(schemaName, TreeNodeType.Schema);
                    connectionNode.Children.Add(schemaNode);
                }

                // Auto-expand 'public' if present
                var publicNode = connectionNode.Children.FirstOrDefault(n => n.Label == "public");
                if (publicNode is not null)
                {
                    await LoadSchemaChildrenAsync(publicNode, "public", cancellationToken);
                    publicNode.IsExpanded = true;
                }
            }
            else
            {
                // Single-schema or no-schema DB — flat Tables/Views
                await LoadTablesAndViewsIntoNode(connectionNode, cancellationToken);
            }

            connectionNode.IsExpanded = true;
            RootNodes.Add(connectionNode);
            OnPropertyChanged(nameof(ActiveProvider));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error($"Failed to open connection '{config.Name}'", ex);

            _activeProvider = null;
            RootNodes.Clear();
            OnPropertyChanged(nameof(ActiveProvider));
        }
    }

    public async Task DisconnectAsync()
    {
        ErrorMessage = null;
        if (_activeProvider is not null)
        {
            try
            {
                await _activeProvider.DisposeAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Ignoring provider dispose failure on disconnect: {ex.Message}");
            }
        }

        _activeProvider = null;
        RootNodes.Clear();
        OnPropertyChanged(nameof(ActiveProvider));
        AppLog.Info("Disconnected current provider");
    }

    public async Task ExpandSchemaNodeAsync(TreeNodeViewModel schemaNode, CancellationToken cancellationToken = default)
    {
        if (schemaNode.NodeType != TreeNodeType.Schema) return;
        if (schemaNode.Children.Count > 0) return; // already loaded

        await LoadSchemaChildrenAsync(schemaNode, schemaNode.Label, cancellationToken);
    }

    private async Task LoadSchemaChildrenAsync(TreeNodeViewModel parentNode, string schemaName, CancellationToken cancellationToken)
    {
        if (_activeProvider is null) return;

        try
        {
            var tablesNode = new TreeNodeViewModel("Tables", TreeNodeType.Folder);
            var viewsNode = new TreeNodeViewModel("Views", TreeNodeType.Folder);

            var tablesSql = $"SELECT tablename FROM pg_tables WHERE schemaname = '{EscapeString(schemaName)}' ORDER BY tablename";
            var viewsSql = $"SELECT viewname FROM pg_views WHERE schemaname = '{EscapeString(schemaName)}' ORDER BY viewname";

            var tablesResult = await _activeProvider.ExecuteAsync(tablesSql, cancellationToken);
            foreach (var r in tablesResult.Rows)
                tablesNode.Children.Add(new TreeNodeViewModel(r[0]?.ToString() ?? "", TreeNodeType.Table));

            var viewsResult = await _activeProvider.ExecuteAsync(viewsSql, cancellationToken);
            foreach (var r in viewsResult.Rows)
                viewsNode.Children.Add(new TreeNodeViewModel(r[0]?.ToString() ?? "", TreeNodeType.View));

            parentNode.Children.Add(tablesNode);
            parentNode.Children.Add(viewsNode);

            tablesNode.IsExpanded = true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load schema '{schemaName}'", ex);
        }
    }

    private async Task LoadTablesAndViewsIntoNode(TreeNodeViewModel parentNode, CancellationToken cancellationToken)
    {
        if (_activeProvider is null) return;

        var tablesNode = new TreeNodeViewModel("Tables", TreeNodeType.Folder);
        var tables = await _activeProvider.GetTablesAsync(cancellationToken);
        foreach (var t in tables)
            tablesNode.Children.Add(new TreeNodeViewModel(t.Name, TreeNodeType.Table));

        var viewsNode = new TreeNodeViewModel("Views", TreeNodeType.Folder);
        var views = await _activeProvider.GetViewsAsync(cancellationToken);
        foreach (var v in views)
            viewsNode.Children.Add(new TreeNodeViewModel(v.Name, TreeNodeType.View));

        parentNode.Children.Add(tablesNode);
        parentNode.Children.Add(viewsNode);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        if (_activeProvider is null) return;

        var connectionNode = RootNodes.FirstOrDefault();
        if (connectionNode is null) return;

        try
        {
            // Reload schemas or flat tables
            connectionNode.Children.Clear();
            var schemas = await _activeProvider.GetSchemasAsync();

            if (schemas.Count > 1)
            {
                foreach (var schemaName in schemas)
                    connectionNode.Children.Add(new TreeNodeViewModel(schemaName, TreeNodeType.Schema));
            }
            else
            {
                await LoadTablesAndViewsIntoNode(connectionNode, default);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("Refresh failed", ex);
        }
    }

    private static string EscapeString(string input) => input.Replace("'", "''");
}

public enum TreeNodeType { Connection, Folder, Table, View, Procedure, Schema }

public partial class TreeNodeViewModel : ViewModelBase
{
    public string Label { get; }
    public TreeNodeType NodeType { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NodeIconKind))]
    private bool _isExpanded;

    public Material.Icons.MaterialIconKind NodeIconKind => NodeType switch
    {
        TreeNodeType.Connection => IsExpanded
            ? Material.Icons.MaterialIconKind.Server
            : Material.Icons.MaterialIconKind.ServerOutline,
        TreeNodeType.Schema     => Material.Icons.MaterialIconKind.DatabaseOutline,
        TreeNodeType.Folder     => IsExpanded
            ? Material.Icons.MaterialIconKind.FolderOpenOutline
            : Material.Icons.MaterialIconKind.FolderOutline,
        TreeNodeType.Table      => Material.Icons.MaterialIconKind.TableLarge,
        TreeNodeType.View       => Material.Icons.MaterialIconKind.TableEye,
        TreeNodeType.Procedure  => Material.Icons.MaterialIconKind.CodeBraces,
        _                       => Material.Icons.MaterialIconKind.Database
    };

    // Remove string NodeIcon — replaced by MaterialIconKind above
    public TreeNodeViewModel(string label, TreeNodeType nodeType)
    {
        Label = label;
        NodeType = nodeType;
    }
}
