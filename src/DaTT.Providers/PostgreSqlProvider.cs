using System.Data.Common;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DaTT.Providers;

public sealed class PostgreSqlProvider : BaseSqlProvider
{
    private readonly PostgreSqlDialect _dialect = new();

    public PostgreSqlProvider(ILogger<PostgreSqlProvider> logger) : base(logger) { }

    public override string EngineName => "PostgreSQL";
    public override string[] SupportedSchemes => ["postgresql", "postgres"];
    public override ISqlDialect Dialect => _dialect;

    protected override DbConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);

    protected override string ConvertConnectionString(string uri)
    {
        var u = new Uri(uri);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = u.Host,
            Port = u.Port > 0 ? u.Port : 5432,
            Database = u.AbsolutePath.TrimStart('/'),
        };
        var userInfo = u.UserInfo.Split(':');
        if (userInfo.Length > 0 && !string.IsNullOrEmpty(userInfo[0]))
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        return builder.ConnectionString;
    }

    protected override string BuildPagedQuery(string table, string where, string order, int offset, int limit)
        => $"SELECT * FROM {QuoteIdentifier(table)}{where}{order} LIMIT {limit} OFFSET {offset}";

    public override async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowDatabases(), ct);
        return result.Rows.Select(r => r[0]?.ToString() ?? "").ToList();
    }

    public override async Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(
            "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name", ct);
        return result.Rows.Select(r => r[0]?.ToString() ?? "").ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowTables("public"), ct);
        return result.Rows.Select(r => new TableInfo(
            Name: r[0]?.ToString() ?? "",
            Comment: r.Length > 1 ? r[1]?.ToString() : null
        )).ToList();
    }

    public override async Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowViews("public"), ct);
        return result.Rows.Select(r => new TableInfo(Name: r[0]?.ToString() ?? "")).ToList();
    }

    public override async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT column_name, data_type, is_nullable, column_default, ordinal_position,
                   character_maximum_length,
                   (SELECT COUNT(*) FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu USING (constraint_name, table_schema, table_name)
                    WHERE tc.constraint_type = 'PRIMARY KEY' AND kcu.column_name = c.column_name
                      AND tc.table_name = c.table_name) > 0 AS is_pk
            FROM information_schema.columns c
            WHERE table_schema = 'public' AND table_name = '{Escape(table)}'
            ORDER BY ordinal_position
            """;
        var result = await ExecuteAsync(sql, ct);
        return result.Rows.Select(r => new ColumnMeta(
            Name: r[0]?.ToString() ?? "",
            DataType: r[1]?.ToString() ?? "",
            SimpleType: r[1]?.ToString(),
            IsNullable: r[2]?.ToString() == "YES",
            DefaultValue: r[3]?.ToString(),
            OrdinalPosition: Convert.ToInt32(r[4]),
            MaxLength: r[5] is not null and not DBNull ? Convert.ToInt32(r[5]) : null,
            Key: Convert.ToBoolean(r[6]) ? "PRIMARY KEY" : null
        )).ToList();
    }

    public override async Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT i.relname, a.attname, ix.indisunique, ix.indisprimary
            FROM pg_class t
            JOIN pg_index ix ON t.oid = ix.indrelid
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE t.relname = '{Escape(table)}'
            ORDER BY i.relname, a.attnum
            """;
        var result = await ExecuteAsync(sql, ct);
        return result.Rows
            .GroupBy(r => r[0]?.ToString() ?? "")
            .Select(g => new IndexMeta(
                Name: g.Key,
                Columns: g.Select(r => r[1]?.ToString() ?? "").ToList(),
                IsUnique: Convert.ToBoolean(g.First()[2]),
                IsPrimaryKey: Convert.ToBoolean(g.First()[3])
            )).ToList();
    }

    public override async Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT tc.constraint_name, kcu.column_name, ccu.table_name, ccu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu USING (constraint_name, table_schema, table_name)
            JOIN information_schema.constraint_column_usage ccu USING (constraint_name, table_schema)
            WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = '{Escape(table)}'
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
        var result = await ExecuteAsync(_dialect.ShowTriggers("public"), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "TRIGGER")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowProcedures("public"), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "PROCEDURE")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowFunctions("public"), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "FUNCTION")).ToList();
    }

    public override async Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowUsers(), ct);
        return result.Rows.Select(r => new DatabaseObjectInfo(r[0]?.ToString() ?? "", "USER")).ToList();
    }

    public override async Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowViewSource("public", view), ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowProcedureSource("public", name), ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowFunctionSource("public", name), ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(_dialect.ShowTriggerSource("public", name), ct);
        return result.Rows.FirstOrDefault()?[0]?.ToString();
    }

    public override async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var cols = string.Join(", ", values.Keys.Select(k => QuoteIdentifier(k)));
        var vals = string.Join(", ", values.Values.Select(FormatValue));
        await ExecuteAsync($"INSERT INTO {QuoteIdentifier(table)} ({cols}) VALUES ({vals})", ct);
    }

    public override async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var set = string.Join(", ", newValues.Select(kv => $"{QuoteIdentifier(kv.Key)} = {FormatValue(kv.Value)}"));
        var where = string.Join(" AND ", pkValues.Select(kv => $"{QuoteIdentifier(kv.Key)} = {FormatValue(kv.Value)}"));
        await ExecuteAsync($"UPDATE {QuoteIdentifier(table)} SET {set} WHERE {where}", ct);
    }

    public override async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var where = string.Join(" AND ", pkValues.Select(kv => $"{QuoteIdentifier(kv.Key)} = {FormatValue(kv.Value)}"));
        await ExecuteAsync($"DELETE FROM {QuoteIdentifier(table)} WHERE {where}", ct);
    }

    public override async Task CreateTableAsync(string ddl, CancellationToken ct = default)
        => await ExecuteAsync(ddl, ct);

    public override async Task DropTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"DROP TABLE {QuoteIdentifier(table)}", ct);

    public override async Task TruncateTableAsync(string table, CancellationToken ct = default)
        => await ExecuteAsync($"TRUNCATE TABLE {QuoteIdentifier(table)}", ct);

    public override async Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => await ExecuteAsync($"ALTER TABLE {QuoteIdentifier(currentName)} RENAME TO {QuoteIdentifier(newName)}", ct);

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{Escape(s)}'",
        bool b => b ? "TRUE" : "FALSE",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
        _ => value.ToString() ?? "NULL"
    };

    private static string Escape(string input) => input.Replace("'", "''");
}
