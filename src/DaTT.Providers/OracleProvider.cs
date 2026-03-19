using System.Data.Common;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DaTT.Providers;

public sealed class OracleProvider : BaseSqlProvider
{
    private readonly OracleDialect _dialect = new();

    public OracleProvider(ILogger<OracleProvider> logger) : base(logger) { }

    public override string EngineName => "Oracle";
    public override string[] SupportedSchemes => ["jdbc:oracle:thin"];
    public override ISqlDialect Dialect => _dialect;

    protected override DbConnection CreateConnection(string connectionString)
        => new OracleConnection(connectionString);

    protected override string ConvertConnectionString(string uri)
    {
        var withoutPrefix = uri.Replace("jdbc:oracle:thin:@tcp://", "");
        var parts = withoutPrefix.Split('/');
        var hostPort = parts[0].Split(':');
        var host = hostPort[0];
        var port = hostPort.Length > 1 ? hostPort[1] : "1521";
        var serviceOrSid = parts.Length > 1 ? parts[1] : "";

        return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={serviceOrSid})));User Id=;Password=;";
    }

    protected override string BuildPagedQuery(string table, string where, string order, int offset, int limit)
        => $"SELECT * FROM (SELECT a.*, ROWNUM rnum FROM (SELECT * FROM \"{table}\"{where}{order}) a WHERE ROWNUM <= {offset + limit}) WHERE rnum > {offset}";

    public override async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowDatabases(), ct);
        return result.Rows.Select(r => r[0]?.ToString() ?? "").ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            "SELECT TABLE_NAME, COMMENTS, NUM_ROWS FROM USER_TAB_COMMENTS utc LEFT JOIN USER_TABLES ut ON utc.TABLE_NAME = ut.TABLE_NAME WHERE utc.TABLE_TYPE = 'TABLE' ORDER BY utc.TABLE_NAME", ct);
        return result.Rows.Select(r => new TableInfo(
            Name: r[0]?.ToString() ?? "",
            Comment: r[1]?.ToString(),
            RowCount: r[2] is not null and not DBNull ? Convert.ToInt64(r[2]) : null
        )).ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            "SELECT VIEW_NAME FROM USER_VIEWS ORDER BY VIEW_NAME", ct);
        return result.Rows.Select(r => new TableInfo(Name: r[0]?.ToString() ?? "")).ToList();
    }

    public override async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        var upper = Escape(table.ToUpperInvariant());
        var sql = $"""
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.DATA_TYPE, col_com.COMMENTS,
                   NVL2(p.COLUMN_NAME, 'PRI', NULL),
                   c.NULLABLE, c.DATA_LENGTH, c.DATA_DEFAULT, NULL AS EXTRA, c.COLUMN_ID
            FROM USER_TAB_COLUMNS c
            LEFT JOIN USER_COL_COMMENTS col_com ON c.TABLE_NAME = col_com.TABLE_NAME AND c.COLUMN_NAME = col_com.COLUMN_NAME
            LEFT JOIN (
                SELECT cc.COLUMN_NAME FROM USER_CONSTRAINTS uc
                JOIN USER_CONS_COLUMNS cc ON uc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
                WHERE uc.CONSTRAINT_TYPE = 'P' AND uc.TABLE_NAME = '{upper}'
            ) p ON c.COLUMN_NAME = p.COLUMN_NAME
            WHERE c.TABLE_NAME = '{upper}'
            ORDER BY c.COLUMN_ID
            """;
        var result = await ExecuteAsync(sql, ct);
        return result.Rows.Select((r, i) => new ColumnMeta(
            Name: r[0]?.ToString() ?? "",
            DataType: r[1]?.ToString() ?? "",
            SimpleType: r[2]?.ToString(),
            Comment: r[3]?.ToString(),
            Key: r[4]?.ToString(),
            IsNullable: r[5]?.ToString() == "Y",
            MaxLength: r[6] is not null and not DBNull ? Convert.ToInt32(r[6]) : null,
            DefaultValue: r[7]?.ToString(),
            Extra: r[8]?.ToString(),
            OrdinalPosition: r[9] is not null and not DBNull ? Convert.ToInt32(r[9]) : i
        )).ToList();
    }

    public override async Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
    {
        var upper = Escape(table.ToUpperInvariant());
        var sql = $"""
            SELECT i.INDEX_NAME, ic.COLUMN_NAME, i.UNIQUENESS, i.INDEX_TYPE
            FROM USER_INDEXES i
            JOIN USER_IND_COLUMNS ic ON i.INDEX_NAME = ic.INDEX_NAME
            WHERE i.TABLE_NAME = '{upper}'
            ORDER BY i.INDEX_NAME, ic.COLUMN_POSITION
            """;
        var result = await ExecuteAsync(sql, ct);
        return result.Rows
            .GroupBy(r => r[0]?.ToString() ?? "")
            .Select(g => new IndexMeta(
                Name: g.Key,
                Columns: g.Select(r => r[1]?.ToString() ?? "").ToList(),
                IsUnique: g.First()[2]?.ToString() == "UNIQUE",
                IsPrimaryKey: false,
                IndexType: g.First()[3]?.ToString()
            )).ToList();
    }

    public override async Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default)
    {
        var upper = Escape(table.ToUpperInvariant());
        var sql = $"""
            SELECT uc.CONSTRAINT_NAME, cc.COLUMN_NAME, rc.TABLE_NAME, rcc.COLUMN_NAME
            FROM USER_CONSTRAINTS uc
            JOIN USER_CONS_COLUMNS cc ON uc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
            JOIN USER_CONSTRAINTS rc ON uc.R_CONSTRAINT_NAME = rc.CONSTRAINT_NAME
            JOIN USER_CONS_COLUMNS rcc ON rc.CONSTRAINT_NAME = rcc.CONSTRAINT_NAME
            WHERE uc.CONSTRAINT_TYPE = 'R' AND uc.TABLE_NAME = '{upper}'
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
        var result = await ExecuteAsync("SELECT TRIGGER_NAME FROM USER_TRIGGERS ORDER BY TRIGGER_NAME", ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "TRIGGER")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            "SELECT OBJECT_NAME FROM USER_PROCEDURES WHERE OBJECT_TYPE = 'PROCEDURE' ORDER BY OBJECT_NAME", ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "PROCEDURE")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            "SELECT OBJECT_NAME FROM USER_PROCEDURES WHERE OBJECT_TYPE = 'FUNCTION' ORDER BY OBJECT_NAME", ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "FUNCTION")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowUsers(), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "USER")).ToList();
    }

    public override async Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            $"SELECT DBMS_METADATA.GET_DDL('TABLE', '{Escape(table.ToUpperInvariant())}') FROM DUAL", ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            $"SELECT TEXT FROM USER_VIEWS WHERE VIEW_NAME = '{Escape(view.ToUpperInvariant())}'", ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            $"SELECT TEXT FROM USER_SOURCE WHERE NAME = '{Escape(name.ToUpperInvariant())}' AND TYPE = 'PROCEDURE' ORDER BY LINE", ct);
        return string.Join("", result.Rows.Select(r => r[0]?.ToString() ?? ""));
    }

    public override async Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            $"SELECT TEXT FROM USER_SOURCE WHERE NAME = '{Escape(name.ToUpperInvariant())}' AND TYPE = 'FUNCTION' ORDER BY LINE", ct);
        return string.Join("", result.Rows.Select(r => r[0]?.ToString() ?? ""));
    }

    public override async Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            $"SELECT TEXT FROM USER_SOURCE WHERE NAME = '{Escape(name.ToUpperInvariant())}' AND TYPE = 'TRIGGER' ORDER BY LINE", ct);
        return string.Join("", result.Rows.Select(r => r[0]?.ToString() ?? ""));
    }

    public override async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var cols = string.Join(", ", values.Keys.Select(k => $"\"{k}\""));
        var vals = string.Join(", ", values.Values.Select(FormatValue));
        await ExecuteAsync($"INSERT INTO \"{table}\" ({cols}) VALUES ({vals})", ct);
    }

    public override async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var set = string.Join(", ", newValues.Select(kv => $"\"{kv.Key}\" = {FormatValue(kv.Value)}"));
        var where = BuildWhereClause(pkValues);
        await ExecuteAsync($"UPDATE \"{table}\" SET {set} WHERE {where}", ct);
    }

    public override async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var where = BuildWhereClause(pkValues);
        await ExecuteAsync($"DELETE FROM \"{table}\" WHERE {where}", ct);
    }

    public override async Task CreateTableAsync(string ddl, CancellationToken ct = default)
        => await ExecuteAsync(ddl, ct);

    public override async Task DropTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"DROP TABLE \"{table}\"", ct);

    public override async Task TruncateTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"TRUNCATE TABLE \"{table}\"", ct);

    public override async Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => await ExecuteAsync($"ALTER TABLE \"{currentName}\" RENAME TO \"{newName}\"", ct);

    private static string BuildWhereClause(IReadOnlyDictionary<string, object?> pkValues)
        => string.Join(" AND ", pkValues.Select(kv => $"\"{kv.Key}\" = {FormatValue(kv.Value)}"));

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{Escape(s)}'",
        bool b => b ? "1" : "0",
        DateTime dt => $"TO_DATE('{dt:yyyy-MM-dd HH:mm:ss}', 'YYYY-MM-DD HH24:MI:SS')",
        _ => value.ToString() ?? "NULL"
    };

    private static string Escape(string input) => input.Replace("'", "''");
}
