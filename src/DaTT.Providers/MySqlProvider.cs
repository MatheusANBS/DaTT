using System.Data.Common;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DaTT.Providers;

public class MySqlProvider : BaseSqlProvider
{
    private readonly bool _isMariaDb;
    private readonly ISqlDialect _dialect;

    public MySqlProvider(ILogger<MySqlProvider> logger, bool isMariaDb = false) : base(logger)
    {
        _isMariaDb = isMariaDb;
        _dialect = isMariaDb ? new MariaDbDialect() : new MysqlDialect();
    }

    public override string EngineName => _isMariaDb ? "MariaDB" : "MySQL";
    public override string[] SupportedSchemes => _isMariaDb ? ["mariadb"] : ["mysql"];
    public override ISqlDialect Dialect => _dialect;

    protected override DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    protected override string ConvertConnectionString(string uri)
    {
        var builder = new MySqlConnectionStringBuilder();
        var u = new Uri(uri);
        builder.Server = u.Host;
        builder.Port = (uint)(u.Port > 0 ? u.Port : 3306);
        builder.Database = u.AbsolutePath.TrimStart('/');
        var userInfo = u.UserInfo.Split(':');
        builder.UserID = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        return builder.ConnectionString;
    }

    protected override string QuoteIdentifier(string name) => $"`{name}`";

    protected override string BuildPagedQuery(string table, string where, string order, int offset, int limit)
        => $"SELECT * FROM `{table}`{where}{order} LIMIT {limit} OFFSET {offset}";

    public override async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync("SHOW DATABASES", ct);
        return result.Rows.Select(r => r[0]?.ToString() ?? "").ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var db = Connection!.Database;
        var result = await ExecuteAsync(_dialect.ShowTables(db), ct);
        return result.Rows.Select(r => new TableInfo(
            Name: r[0]?.ToString() ?? "",
            Comment: r.Length > 1 ? r[1]?.ToString() : null,
            RowCount: r.Length > 2 && r[2] is not null and not DBNull ? Convert.ToInt64(r[2]) : null
        )).ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = DATABASE()", ct);
        return result.Rows.Select(r => new TableInfo(Name: r[0]?.ToString() ?? "")).ToList();
    }

    public override async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var db = Connection!.Database;
        var result = await ExecuteAsync(_dialect.ShowColumns(db, table), ct);
        return result.Rows.Select((r, i) => new ColumnMeta(
            Name: r[0]?.ToString() ?? "",
            DataType: r[2]?.ToString() ?? "",
            SimpleType: r[1]?.ToString(),
            Comment: r[3]?.ToString(),
            Key: r[4]?.ToString(),
            IsNullable: r[5]?.ToString() == "YES",
            MaxLength: r[6] is not null and not DBNull ? Convert.ToInt32(r[6]) : null,
            DefaultValue: r[7]?.ToString(),
            Extra: r[8]?.ToString(),
            OrdinalPosition: i
        )).ToList();
    }

    public override async Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var db = Connection!.Database;
        var sql = $"""
            SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE, INDEX_TYPE
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = '{Escape(db)}' AND TABLE_NAME = '{Escape(table)}'
            ORDER BY INDEX_NAME, SEQ_IN_INDEX
            """;
        var result = await ExecuteAsync(sql, ct);

        return result.Rows
            .GroupBy(r => r[0]?.ToString() ?? "")
            .Select(g => new IndexMeta(
                Name: g.Key,
                Columns: g.Select(r => r[1]?.ToString() ?? "").ToList(),
                IsUnique: g.First()[2]?.ToString() == "0",
                IsPrimaryKey: g.Key == "PRIMARY",
                IndexType: g.First()[3]?.ToString()
            )).ToList();
    }

    public override async Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT CONSTRAINT_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = '{Escape(table)}'
              AND REFERENCED_TABLE_NAME IS NOT NULL
            """;
        var result = await ExecuteAsync(sql, ct);
        return result.Rows.Select(r => new ForeignKeyMeta(
            Name: r[0]?.ToString() ?? "",
            SourceColumn: r[1]?.ToString() ?? "",
            ReferencedTable: r[2]?.ToString() ?? "",
            ReferencedColumn: r[3]?.ToString() ?? ""
        )).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetTriggersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowTriggers(Connection!.Database), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "TRIGGER")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowProcedures(Connection!.Database), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "PROCEDURE")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowFunctions(Connection!.Database), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "FUNCTION")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowUsers(), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "USER")).ToList();
    }

    public override async Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync($"SHOW CREATE TABLE `{Escape(table)}`", ct);
        return result.Rows.FirstOrDefault()?[1]?.ToString();
    }

    public override async Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync($"SHOW CREATE VIEW `{Escape(view)}`", ct);
        return result.Rows.FirstOrDefault()?[1]?.ToString();
    }

    public override async Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowProcedureSource(Connection!.Database, name), ct);
        return result.Rows.FirstOrDefault()?[2]?.ToString();
    }

    public override async Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowFunctionSource(Connection!.Database, name), ct);
        return result.Rows.FirstOrDefault()?[2]?.ToString();
    }

    public override async Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
    {
        EnsureConnected();
        var result = await ExecuteAsync(_dialect.ShowTriggerSource(Connection!.Database, name), ct);
        return result.Rows.FirstOrDefault()?[2]?.ToString();
    }

    public override async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var cols = string.Join(", ", values.Keys.Select(k => $"`{k}`"));
        var vals = string.Join(", ", values.Values.Select(FormatValue));
        await ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES ({vals})", ct);
    }

    public override async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var set = string.Join(", ", newValues.Select(kv => $"`{kv.Key}` = {FormatValue(kv.Value)}"));
        var where = BuildWhereClause(pkValues);
        await ExecuteAsync($"UPDATE `{table}` SET {set} WHERE {where}", ct);
    }

    public override async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var where = BuildWhereClause(pkValues);
        await ExecuteAsync($"DELETE FROM `{table}` WHERE {where}", ct);
    }

    public override async Task CreateTableAsync(string ddl, CancellationToken ct = default)
        => await ExecuteAsync(ddl, ct);

    public override async Task DropTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"DROP TABLE `{table}`", ct);

    public override async Task TruncateTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"TRUNCATE TABLE `{table}`", ct);

    public override async Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => await ExecuteAsync($"RENAME TABLE `{currentName}` TO `{newName}`", ct);

    private static string BuildWhereClause(IReadOnlyDictionary<string, object?> pkValues)
        => string.Join(" AND ", pkValues.Select(kv => $"`{kv.Key}` = {FormatValue(kv.Value)}"));

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{Escape(s)}'",
        bool b => b ? "1" : "0",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
        _ => value.ToString() ?? "NULL"
    };

    private static string Escape(string input) => input.Replace("'", "''");
}
