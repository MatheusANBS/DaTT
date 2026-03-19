using DaTT.Core.Models;

namespace DaTT.Core.Interfaces;

public interface IDatabaseProvider : IAsyncDisposable
{
    string EngineName { get; }
    string[] SupportedSchemes { get; }
    ISqlDialect Dialect { get; }
    bool IsConnected { get; }

    Task ConnectAsync(string connectionString, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default);
    Task PingAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default);
    Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default);
    Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default);

    Task<IReadOnlyList<DatabaseObjectInfo>> GetTriggersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default);

    Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default);
    Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default);
    Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default);
    Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default);
    Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default);

    Task<ExecuteResult> ExecuteAsync(string sql, CancellationToken ct = default);

    Task<PagedResult<IReadOnlyList<object?[]>>> GetRowsAsync(
        string table,
        int page,
        int pageSize,
        string? filter = null,
        string? orderBy = null,
        CancellationToken ct = default);

    Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);
    Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default);
    Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default);

    Task CreateTableAsync(string ddl, CancellationToken ct = default);
    Task DropTableAsync(string table, CancellationToken ct = default);
    Task TruncateTableAsync(string table, CancellationToken ct = default);
    Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default);
}
