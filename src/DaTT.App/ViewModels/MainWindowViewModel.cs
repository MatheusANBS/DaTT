using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.App.Infrastructure;
using DaTT.Core.Models;

namespace DaTT.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ConnectionManagerViewModel _connectionManager;

    [ObservableProperty]
    private ObjectExplorerViewModel _objectExplorer;

    public ObservableCollection<TabViewModel> OpenTabs { get; } = [];

    [ObservableProperty]
    private TabViewModel? _activeTab;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateTooltip = string.Empty;

    private string _updateUrl = string.Empty;

    public MainWindowViewModel(
        ConnectionManagerViewModel connectionManager,
        ObjectExplorerViewModel objectExplorer)
    {
        _connectionManager = connectionManager;
        _objectExplorer = objectExplorer;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info.HasUpdate)
            {
                UpdateAvailable = true;
                _updateUrl = info.DownloadUrl;
                UpdateTooltip = $"Version {info.LatestVersion} is available — click to download";
                AppLog.Info($"Update available: {info.CurrentVersion} → {info.LatestVersion}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update check failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (!string.IsNullOrEmpty(_updateUrl))
            Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenConnectionManager()
    {
        var existing = OpenTabs.OfType<ConnectionManagerViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        OpenTabs.Add(ConnectionManager);
        ActiveTab = ConnectionManager;
    }

    [RelayCommand]
    private async Task CloseTabAsync(TabViewModel tab)
    {
        if (tab is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();

        OpenTabs.Remove(tab);
    }

    [RelayCommand]
    private async Task CloseAllTabsAsync()
    {
        var tabs = OpenTabs.ToList();
        foreach (var tab in tabs)
        {
            if (tab is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
        OpenTabs.Clear();
        ActiveTab = null;
    }

    [RelayCommand]
    private async Task CloseOtherTabsAsync(TabViewModel keepTab)
    {
        var toClose = OpenTabs.Where(t => t != keepTab).ToList();
        foreach (var tab in toClose)
        {
            if (tab is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            OpenTabs.Remove(tab);
        }
        ActiveTab = keepTab;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            var config = ConnectionManager.SelectedConnection;
            if (config is null) return;

            var effectiveConnectionString = ConnectionManager.GetEffectiveConnectionString(config);
            if (string.IsNullOrWhiteSpace(effectiveConnectionString))
            {
                ErrorMessage = "Selected connection is missing connection settings.";
                return;
            }

            if (effectiveConnectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Use SSH Workspace for ssh:// connections.";
                return;
            }

            config.ConnectionString = effectiveConnectionString;
            await ObjectExplorer.LoadConnectionAsync(config);
            ErrorMessage = ObjectExplorer.ErrorMessage;

            if (ObjectExplorer.ActiveProvider is null && string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = "Failed to open connection.";
            }

            if (ObjectExplorer.ActiveProvider is not null)
            {
                IsConnected = true;
                ConnectionStatusText = $"Connected: {config.Name} ({ObjectExplorer.ActiveProvider.EngineName})";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await ObjectExplorer.DisconnectAsync();
        OpenTabs.Clear();
        ActiveTab = null;

        IsConnected = false;
        ConnectionStatusText = "Disconnected";
    }

    [RelayCommand]
    private async Task OpenTableAsync(TreeNodeViewModel node)
    {
        if (node.NodeType != TreeNodeType.Table) return;

        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null) return;

        var existing = OpenTabs.OfType<DataGridTabViewModel>()
            .FirstOrDefault(t => t.Title == node.Label);

        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var tab = new DataGridTabViewModel(provider, node.Label);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        await tab.LoadAsync();
    }

    [RelayCommand]
    private async Task OpenQueryEditorAsync()
    {
        var provider = await EnsureProviderForEngineAsync(null);
        if (provider is null)
        {
            ErrorMessage = "Connect a non-SSH data source before opening query editor.";
            return;
        }

        var tab = new QueryEditorTabViewModel(provider);
        OpenTabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private async Task OpenSchemaDiffAsync()
    {
        var provider = await EnsureProviderForEngineAsync(null);
        if (provider is null)
            return;

        var existing = OpenTabs.OfType<SchemaDiffTabViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var tab = new SchemaDiffTabViewModel(provider);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        await tab.InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenMonitorAsync()
    {
        var provider = await EnsureProviderForEngineAsync(null);
        if (provider is null)
            return;

        var existing = OpenTabs.OfType<ServerMonitorTabViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var tab = new ServerMonitorTabViewModel(provider);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        await tab.InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenRedisConsoleAsync()
    {
        var provider = await EnsureProviderForEngineAsync("Redis");
        if (provider is null)
            return;

        if (!provider.EngineName.Contains("redis", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Redis Console is available only for an active Redis connection.";
            return;
        }

        var existing = OpenTabs.OfType<RedisConsoleTabViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var tab = new RedisConsoleTabViewModel(provider);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        await tab.InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenElasticConsoleAsync()
    {
        var provider = await EnsureProviderForEngineAsync("ElasticSearch");
        if (provider is null)
            return;

        if (!provider.EngineName.Contains("elastic", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Elastic Console is available only for an active ElasticSearch connection.";
            return;
        }

        var existing = OpenTabs.OfType<ElasticConsoleTabViewModel>().FirstOrDefault();
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var tab = new ElasticConsoleTabViewModel(provider);
        OpenTabs.Add(tab);
        ActiveTab = tab;
        await tab.InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenSshWorkspaceAsync()
    {
        var config = ConnectionManager.SelectedConnection;
        if (config is null)
        {
            ErrorMessage = "Select an SSH connection first.";
            return;
        }

        var effectiveConnectionString = ConnectionManager.GetEffectiveConnectionString(config);
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            ErrorMessage = "Selected SSH connection has no valid settings.";
            return;
        }

        if (!effectiveConnectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "SSH Workspace requires a connection string starting with ssh://.";
            return;
        }

        var existing = OpenTabs.OfType<SshWorkspaceTabViewModel>()
            .FirstOrDefault(t => t.Title.EndsWith(config.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        try
        {
            var settings = SshConnectionSettings.Parse(config.Name, effectiveConnectionString);
            var tab = new SshWorkspaceTabViewModel(settings);
            OpenTabs.Add(tab);
            ActiveTab = tab;
            ErrorMessage = null;

            IsConnected = true;
            ConnectionStatusText = $"SSH Workspace: {config.Name}";

            await tab.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void OpenSelectTemplate(TreeNodeViewModel node)
    {
        if (node.NodeType != TreeNodeType.Table)
            return;

        var script = $"SELECT *{Environment.NewLine}FROM {QuoteIdentifier(node.Label)}{Environment.NewLine}LIMIT 100;";
        OpenQueryEditorWithScript(script);
    }

    public void OpenCountTemplate(TreeNodeViewModel node)
    {
        if (node.NodeType != TreeNodeType.Table)
            return;

        var script = $"SELECT COUNT(*) AS total_count{Environment.NewLine}FROM {QuoteIdentifier(node.Label)};";
        OpenQueryEditorWithScript(script);
    }

    public async Task ShowSourceAsync(TreeNodeViewModel node, CancellationToken cancellationToken = default)
    {
        if (node.NodeType != TreeNodeType.Table)
            return;

        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
            return;

        try
        {
            ErrorMessage = null;
            var columns = await provider.GetColumnsAsync(node.Label, cancellationToken);
            var indexes = await provider.GetIndexesAsync(node.Label, cancellationToken);
            var foreignKeys = await provider.GetForeignKeysAsync(node.Label, cancellationToken);
            var script = BuildCreateTableScript(node.Label, columns, indexes, foreignKeys);
            OpenQueryEditorWithScript(script);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<bool> TruncateTableAsync(TreeNodeViewModel node, CancellationToken cancellationToken = default)
    {
        if (node.NodeType != TreeNodeType.Table)
            return false;

        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
            return false;

        try
        {
            ErrorMessage = null;
            await provider.TruncateTableAsync(node.Label, cancellationToken);
            await ObjectExplorer.RefreshCommand.ExecuteAsync(null);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> DropTableAsync(TreeNodeViewModel node, CancellationToken cancellationToken = default)
    {
        if (node.NodeType != TreeNodeType.Table)
            return false;

        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
            return false;

        try
        {
            ErrorMessage = null;
            await provider.DropTableAsync(node.Label, cancellationToken);
            await ObjectExplorer.RefreshCommand.ExecuteAsync(null);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task DumpTableAsync(string tableName, string format, string outputPath, CancellationToken cancellationToken = default)
    {
        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
        {
            ErrorMessage = "No active connection.";
            return;
        }

        ErrorMessage = null;

        try
        {
            var columns = (await provider.GetColumnsAsync(tableName, cancellationToken))
                .Select(c => c.Name)
                .ToList();

            var rows = new List<object?[]>();
            const int pageSize = 1000;
            int page = 1;

            while (true)
            {
                var result = await provider.GetRowsAsync(tableName, page, pageSize, ct: cancellationToken);
                rows.AddRange(result.Data.Select(r => r.ToArray()));
                if (!result.HasNextPage) break;
                page++;
            }

            var normalized = (format ?? "csv").Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "csv":
                    await File.WriteAllTextAsync(outputPath, DataGridTabViewModel.BuildCsv(columns, rows), System.Text.Encoding.UTF8, cancellationToken);
                    break;
                case "json":
                    await File.WriteAllTextAsync(outputPath, DataGridTabViewModel.BuildJson(columns, rows), System.Text.Encoding.UTF8, cancellationToken);
                    break;
                case "sql":
                    await File.WriteAllTextAsync(outputPath, DataGridTabViewModel.BuildInsertSql(tableName, columns, rows), System.Text.Encoding.UTF8, cancellationToken);
                    break;
                case "xlsx":
                    await Task.Run(() => DataGridTabViewModel.BuildXlsx(outputPath, columns, rows), cancellationToken);
                    break;
                default:
                    ErrorMessage = $"Unsupported format: {format}";
                    return;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error($"DumpTable '{tableName}' failed", ex);
        }
    }

    public async Task DumpSchemaAsync(IReadOnlyList<string> tableNames, string mode, string outputFolder, CancellationToken cancellationToken = default)
    {
        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
        {
            ErrorMessage = "No active connection.";
            return;
        }

        ErrorMessage = null;

        try
        {
            var includeStructure = mode is "structure" or "all";
            var includeData = mode is "data" or "all";

            foreach (var tableName in tableNames)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"-- Table: {tableName}");
                sb.AppendLine();

                if (includeStructure)
                {
                    var columns = await provider.GetColumnsAsync(tableName, cancellationToken);
                    var indexes = await provider.GetIndexesAsync(tableName, cancellationToken);
                    var foreignKeys = await provider.GetForeignKeysAsync(tableName, cancellationToken);

                    sb.AppendLine(BuildCreateTableScript(tableName, columns, indexes, foreignKeys));
                    sb.AppendLine();
                }

                if (includeData)
                {
                    var colNames = (await provider.GetColumnsAsync(tableName, cancellationToken))
                        .OrderBy(c => c.OrdinalPosition)
                        .Select(c => c.Name)
                        .ToList();

                    var rows = new List<object?[]>();
                    const int pageSize = 1000;
                    int page = 1;

                    while (true)
                    {
                        var result = await provider.GetRowsAsync(tableName, page, pageSize, ct: cancellationToken);
                        rows.AddRange(result.Data.Select(r => r.ToArray()));
                        if (!result.HasNextPage) break;
                        page++;
                    }

                    if (rows.Count > 0)
                        sb.AppendLine(DataGridTabViewModel.BuildInsertSql(tableName, colNames, rows));
                }

                var safeName = string.Join("_", tableName.Split(Path.GetInvalidFileNameChars()));
                var filePath = Path.Combine(outputFolder, $"{safeName}.sql");
                await File.WriteAllTextAsync(filePath, sb.ToString(), System.Text.Encoding.UTF8, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error($"DumpSchema failed", ex);
        }
    }

    public async Task CreateTableAsync(string ddlScript, CancellationToken cancellationToken = default)
    {
        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
        {
            ErrorMessage = "No active connection.";
            return;
        }

        ErrorMessage = null;

        try
        {
            await provider.ExecuteAsync(ddlScript, cancellationToken);
            await ObjectExplorer.RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("CreateTable failed", ex);
        }
    }

    private void OpenQueryEditorWithScript(string script)
    {
        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
            return;

        var tab = new QueryEditorTabViewModel(provider)
        {
            QueryText = script
        };

        OpenTabs.Add(tab);
        ActiveTab = tab;
    }

    private static string BuildCreateTableScript(
        string tableName,
        IReadOnlyList<ColumnMeta> columns,
        IReadOnlyList<IndexMeta> indexes,
        IReadOnlyList<ForeignKeyMeta> foreignKeys)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var definitions = new List<string>();

        foreach (var column in columns.OrderBy(c => c.OrdinalPosition))
        {
            var line = $"    {QuoteIdentifier(column.Name)} {column.DataType}";
            if (!column.IsNullable)
                line += " NOT NULL";
            if (!string.IsNullOrWhiteSpace(column.DefaultValue))
                line += $" DEFAULT {column.DefaultValue}";

            definitions.Add(line);
        }

        var primaryColumns = columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.OrdinalPosition)
            .Select(c => QuoteIdentifier(c.Name))
            .ToList();

        if (primaryColumns.Count > 0)
            definitions.Add($"    PRIMARY KEY ({string.Join(", ", primaryColumns)})");

        foreach (var fk in foreignKeys)
        {
            definitions.Add(
                $"    CONSTRAINT {QuoteIdentifier(fk.Name)} FOREIGN KEY ({QuoteIdentifier(fk.SourceColumn)}) " +
                $"REFERENCES {QuoteIdentifier(fk.ReferencedTable)} ({QuoteIdentifier(fk.ReferencedColumn)})");
        }

        var lines = new List<string>
        {
            $"CREATE TABLE {quotedTable} (",
            string.Join($",{Environment.NewLine}", definitions),
            ");"
        };

        foreach (var index in indexes.Where(i => !i.IsPrimaryKey))
        {
            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            var indexColumns = string.Join(", ", index.Columns.Select(QuoteIdentifier));
            lines.Add($"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {quotedTable} ({indexColumns});");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return identifier;

        return string.Join('.', parts.Select(static part => $"\"{part.Replace("\"", "\"\"")}\""));
    }

    private async Task<DaTT.Core.Interfaces.IDatabaseProvider?> EnsureProviderForEngineAsync(string? expectedEngine)
    {
        var current = ObjectExplorer.ActiveProvider;
        if (current is not null)
        {
            if (string.IsNullOrWhiteSpace(expectedEngine) ||
                current.EngineName.Equals(expectedEngine, StringComparison.OrdinalIgnoreCase))
                return current;
        }

        var selected = ConnectionManager.SelectedConnection;
        if (selected is null)
        {
            ErrorMessage = "Select and connect a data source first.";
            return null;
        }

        var effectiveConnectionString = ConnectionManager.GetEffectiveConnectionString(selected);
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            ErrorMessage = "Selected connection is missing valid settings.";
            return null;
        }

        if (effectiveConnectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Use SSH Workspace for ssh:// connections.";
            return null;
        }

        selected.ConnectionString = effectiveConnectionString;
        await ObjectExplorer.LoadConnectionAsync(selected);

        var provider = ObjectExplorer.ActiveProvider;
        if (provider is null)
        {
            ErrorMessage = ObjectExplorer.ErrorMessage ?? "Unable to connect selected data source.";
            return null;
        }

        IsConnected = true;
        ConnectionStatusText = $"Connected: {selected.Name} ({provider.EngineName})";

        if (!string.IsNullOrWhiteSpace(expectedEngine) &&
            !provider.EngineName.Equals(expectedEngine, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = $"Selected connection is '{provider.EngineName}', expected '{expectedEngine}'.";
            return null;
        }

        return provider;
    }
}
