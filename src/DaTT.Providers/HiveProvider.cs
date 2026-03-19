using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;

namespace DaTT.Providers;

public sealed class HiveProvider : IDatabaseProvider
{
    private readonly ILogger<HiveProvider> _logger;
    private string? _connectionString;

    public HiveProvider(ILogger<HiveProvider> logger)
    {
        _logger = logger;
    }

    public string EngineName => "Hive";
    public string[] SupportedSchemes => ["jdbc:hive2"];
    public ISqlDialect Dialect => NoSqlDialect.Instance;
    public bool IsConnected => _connectionString is not null;

    public Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        _logger.LogWarning("Hive provider: connection test is a stub. Full Thrift support is post-MVP.");
        return Task.FromResult(false);
    }

    public Task ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        _connectionString = connectionString;
        _logger.LogWarning("Hive provider: full Thrift/Arrow connection support is post-MVP.");
        return Task.CompletedTask;
    }

    public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ColumnMeta>>([]);

    public Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IndexMeta>>([]);

    public Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ForeignKeyMeta>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetTriggersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<ExecuteResult> ExecuteAsync(string sql, CancellationToken ct = default)
        => Task.FromResult(ExecuteResult.FromError("Hive provider is not yet implemented."));

    public Task<PagedResult<IReadOnlyList<object?[]>>> GetRowsAsync(
        string table, int page, int pageSize, string? filter, string? orderBy,
        CancellationToken ct = default)
        => Task.FromResult(new PagedResult<IReadOnlyList<object?[]>>([], 0, page, pageSize));

    public Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CreateTableAsync(string ddl, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DropTableAsync(string table, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task TruncateTableAsync(string table, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
