using System.Data;
using System.Data.Common;
using System.Diagnostics;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using Microsoft.Extensions.Logging;

namespace DaTT.Providers;

public abstract class BaseSqlProvider : IDatabaseProvider
{
    protected DbConnection? Connection;
    protected readonly ILogger Logger;

    public abstract string EngineName { get; }
    public abstract string[] SupportedSchemes { get; }
    public abstract ISqlDialect Dialect { get; }
    public bool IsConnected => Connection is not null && Connection.State == ConnectionState.Open;

    protected BaseSqlProvider(ILogger logger)
    {
        Logger = logger;
    }

    protected abstract DbConnection CreateConnection(string connectionString);
    protected abstract string ConvertConnectionString(string uri);

    public async Task ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        await DisposeAsync();
        var adoString = ConvertConnectionString(connectionString);
        Connection = CreateConnection(adoString);
        await Connection.OpenAsync(ct);
        Logger.LogInformation("{Engine} connection opened", EngineName);
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            var adoString = ConvertConnectionString(connectionString);
            await using var conn = CreateConnection(adoString);
            await conn.OpenAsync(ct);
            return conn.State == ConnectionState.Open;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Engine} connection test failed", EngineName);
            return false;
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        await using var cmd = Connection!.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
    }

    public virtual Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public virtual Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public virtual Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public virtual Task<IReadOnlyList<DatabaseObjectInfo>> GetTriggersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public virtual Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public virtual Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public virtual Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public virtual Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public virtual Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public virtual Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public virtual Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public virtual Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public async Task<ExecuteResult> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cmd = Connection!.CreateCommand();
            cmd.CommandText = sql;

            var isQuery = IsQueryStatement(sql);
            if (isQuery)
            {
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var columns = new List<ColumnMeta>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(new ColumnMeta(
                        Name: reader.GetName(i),
                        DataType: reader.GetDataTypeName(i),
                        OrgTable: reader.GetSchemaTable()?.Rows[i]["BaseTableName"]?.ToString()
                    ));
                }

                var rows = new List<object?[]>();
                while (await reader.ReadAsync(ct))
                {
                    var raw = new object[reader.FieldCount];
                    reader.GetValues(raw);
                    rows.Add(raw.Select(v => v is DBNull ? null : v).ToArray<object?>());
                }

                sw.Stop();
                return ExecuteResult.FromRows(columns, rows, sw.Elapsed);
            }
            else
            {
                var affected = await cmd.ExecuteNonQueryAsync(ct);
                sw.Stop();
                return ExecuteResult.FromAffected(affected, sw.Elapsed);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Query execution failed on {Engine}", EngineName);
            return ExecuteResult.FromError(ex.Message, sw.Elapsed);
        }
    }

    public async Task<PagedResult<IReadOnlyList<object?[]>>> GetRowsAsync(
        string table, int page, int pageSize,
        string? filter = null, string? orderBy = null,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var offset = (page - 1) * pageSize;
        var where = string.IsNullOrWhiteSpace(filter) ? "" : $" WHERE {filter}";
        var order = string.IsNullOrWhiteSpace(orderBy) ? "" : $" ORDER BY {orderBy}";
        var countSql = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)}{where}";
        var dataSql = BuildPagedQuery(table, where, order, offset, pageSize);

        await using var countCmd = Connection!.CreateCommand();
        countCmd.CommandText = countSql;
        var totalRows = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct) ?? 0);

        await using var dataCmd = Connection.CreateCommand();
        dataCmd.CommandText = dataSql;
        await using var reader = await dataCmd.ExecuteReaderAsync(ct);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row!);
            rows.Add(row);
        }

        return new PagedResult<IReadOnlyList<object?[]>>(rows, totalRows, page, pageSize);
    }

    protected virtual string BuildPagedQuery(string table, string where, string order, int offset, int limit)
        => $"SELECT * FROM {QuoteIdentifier(table)}{where}{order} LIMIT {limit} OFFSET {offset}";

    protected virtual string QuoteIdentifier(string name) => $"\"{name}\"";

    public abstract Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default);
    public abstract Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default);
    public abstract Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);
    public abstract Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default);
    public abstract Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default);
    public abstract Task CreateTableAsync(string ddl, CancellationToken ct = default);
    public abstract Task DropTableAsync(string table, CancellationToken ct = default);
    public abstract Task TruncateTableAsync(string table, CancellationToken ct = default);
    public abstract Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default);

    protected void EnsureConnected()
    {
        if (Connection is null || Connection.State != ConnectionState.Open)
            throw new InvalidOperationException($"{EngineName} connection is not open.");
    }

    private static bool IsQueryStatement(string sql)
    {
        var trimmed = sql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DESC", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync();
            Connection = null;
        }
    }
}
