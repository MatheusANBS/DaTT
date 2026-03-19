using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.Providers.Dialects;

public class PostgreSqlDialect : ISqlDialect
{
    public string ShowDatabases()
        => "SELECT datname AS \"Database\" FROM pg_database WHERE datistemplate = false";

    public string ShowSchemas()
        => "SELECT schema_name AS \"schema\" FROM information_schema.schemata";

    public string ShowTables(string database)
        => $"SELECT t.table_name AS \"name\", pg_catalog.obj_description(pgc.oid, 'pg_class') AS \"comment\" FROM information_schema.tables t JOIN pg_catalog.pg_class pgc ON t.table_name = pgc.relname JOIN pg_catalog.pg_namespace pgn ON pgn.oid = pgc.relnamespace AND pgn.nspname = t.table_schema WHERE t.table_type = 'BASE TABLE' AND t.table_schema = '{database}' ORDER BY t.table_name";

    public string ShowViews(string database)
        => $"SELECT table_name AS \"name\" FROM information_schema.tables WHERE table_schema = '{database}' AND table_type = 'VIEW' ORDER BY table_name";

    public string ShowColumns(string database, string table)
    {
        var view = table.Contains('.') ? table.Split('.')[1] : table;
        return $"SELECT c.COLUMN_NAME \"name\", DATA_TYPE \"simpleType\", DATA_TYPE \"type\", IS_NULLABLE nullable, CHARACTER_MAXIMUM_LENGTH \"maxLength\", COLUMN_DEFAULT \"defaultValue\", '' \"comment\", tc.constraint_type \"key\" FROM information_schema.columns c LEFT JOIN information_schema.constraint_column_usage ccu ON c.COLUMN_NAME = ccu.column_name AND c.table_name = ccu.table_name AND ccu.table_catalog = c.TABLE_CATALOG AND c.table_schema = ccu.table_schema LEFT JOIN information_schema.table_constraints tc ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema AND tc.table_catalog = c.TABLE_CATALOG AND tc.table_name = c.table_name WHERE c.TABLE_SCHEMA = '{database}' AND c.table_name = '{view}' ORDER BY ORDINAL_POSITION";
    }

    public string ShowIndex(string database, string table)
        => $"SELECT indexname AS index_name, indexdef FROM pg_indexes WHERE schemaname = '{database}' AND tablename = '{table}'";

    public string ShowTriggers(string database)
        => $"SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE trigger_schema = '{database}'";

    public string ShowProcedures(string database)
        => $"SELECT ROUTINE_NAME FROM information_schema.routines WHERE ROUTINE_SCHEMA = '{database}' AND ROUTINE_TYPE = 'PROCEDURE'";

    public string ShowFunctions(string database)
        => $"SELECT ROUTINE_NAME FROM information_schema.routines WHERE ROUTINE_SCHEMA = '{database}' AND ROUTINE_TYPE = 'FUNCTION'";

    public string ShowUsers()
        => "SELECT usename AS \"user\" FROM pg_user";

    public string ShowTableSource(string database, string table)
        => string.Empty;

    public string ShowViewSource(string database, string table)
        => $"SELECT CONCAT('CREATE VIEW ', table_name, '\nAS\n(', regexp_replace(view_definition, ';$', ''), ')') AS \"Create View\" FROM information_schema.views WHERE table_schema = '{database}' AND table_name = '{table}'";

    public string ShowProcedureSource(string database, string name)
        => $"SELECT pg_get_functiondef('{database}.{name}'::regproc) AS \"Create Procedure\"";

    public string ShowFunctionSource(string database, string name)
        => $"SELECT pg_get_functiondef('{database}.{name}'::regproc) AS \"Create Function\"";

    public string ShowTriggerSource(string database, string name)
        => $"SELECT pg_get_triggerdef(oid) AS \"SQL Original Statement\" FROM pg_trigger WHERE tgname = '{name}'";

    public string BuildPageSql(string database, string table, int pageSize)
        => $"SELECT * FROM {table} LIMIT {pageSize}";

    public string CountSql(string database, string table)
        => $"SELECT COUNT(*) FROM {table}";

    public string PingDatabase(string database)
        => string.IsNullOrEmpty(database) ? "SELECT 1" : $"SET search_path TO '{database}'";

    public string CreateDatabase(string database)
        => $"CREATE DATABASE \"{database}\"";

    public string TruncateDatabase(string database)
        => $"SELECT CONCAT('TRUNCATE TABLE \"', TABLE_NAME, '\";') AS trun FROM INFORMATION_SCHEMA.TABLES WHERE table_schema = '{database}' AND table_type = 'BASE TABLE'";

    public string AddColumn(string table)
        => $"ALTER TABLE {table} ADD COLUMN [column] [type]";

    public string UpdateColumn(UpdateColumnParam param)
    {
        var nullability = param.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
        var sql = $"ALTER TABLE {param.Table} ALTER COLUMN {param.ColumnName} TYPE {param.ColumnType};\nALTER TABLE {param.Table} ALTER COLUMN {param.ColumnName} {nullability}";
        if (!string.IsNullOrEmpty(param.Comment))
            sql += $";\nCOMMENT ON COLUMN {param.Table}.{param.ColumnName} IS '{param.Comment}'";
        if (param.ColumnName != param.NewColumnName)
            sql += $";\nALTER TABLE {param.Table} RENAME COLUMN {param.ColumnName} TO {param.NewColumnName}";
        return sql;
    }

    public string UpdateTable(UpdateTableParam param)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(param.NewComment) && param.NewComment != param.Comment)
            parts.Add($"COMMENT ON TABLE {param.Table} IS '{param.NewComment}'");
        if (!string.IsNullOrEmpty(param.NewTableName) && param.NewTableName != param.Table)
            parts.Add($"ALTER TABLE {param.Table} RENAME TO {param.NewTableName}");
        return string.Join(";\n", parts);
    }

    public string CreateIndex(CreateIndexParam param)
    {
        var indexType = param.IndexType ?? "btree";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"CREATE INDEX {param.Column}_{timestamp}_index ON {param.Table} USING {indexType} ({param.Column})";
    }

    public string DropIndex(string table, string indexName)
        => $"DROP INDEX {indexName}";

    public string CreateUser()
        => "CREATE USER [name] WITH PASSWORD 'password'";

    public string ProcessList()
        => "SELECT a.pid AS \"Id\", a.usename AS \"User\", a.client_addr AS \"Host\", a.client_port AS \"Port\", datname AS \"db\", query AS \"Command\", l.mode AS \"State\", query_start AS \"Time\" FROM pg_stat_activity a LEFT JOIN pg_locks l ON a.pid = l.pid LEFT JOIN pg_class c ON l.relation = c.oid ORDER BY a.pid ASC";

    public string VariableList() => "SHOW ALL";

    public string StatusList()
        => "SELECT 'db_numbackends' AS db, pg_stat_get_db_numbackends(datid) AS status FROM pg_stat_database WHERE datname = current_database() UNION ALL SELECT 'db_xact_commit', pg_stat_get_db_xact_commit(datid) FROM pg_stat_database WHERE datname = current_database() UNION ALL SELECT 'db_xact_rollback', pg_stat_get_db_xact_rollback(datid) FROM pg_stat_database WHERE datname = current_database()";

    public string TableTemplate()
        => """
           CREATE TABLE [name](
               id SERIAL NOT NULL PRIMARY KEY,
               create_time DATE,
               update_time DATE,
               [column] VARCHAR(255)
           );
           COMMENT ON TABLE [table] IS '[comment]';
           COMMENT ON COLUMN [table].[column] IS '[comment]';
           """;

    public string ViewTemplate()
        => "CREATE VIEW [name]\nAS\n(SELECT * FROM ...)";

    public string ProcedureTemplate()
        => "CREATE PROCEDURE [name]()\nLANGUAGE SQL\nAS $$\n[content]\n$$";

    public string FunctionTemplate()
        => "CREATE FUNCTION [name]()\nRETURNS [type] AS $$\nBEGIN\n    RETURN [type];\nEND;\n$$ LANGUAGE plpgsql;";

    public string TriggerTemplate()
        => "CREATE FUNCTION [tri_fun]() RETURNS TRIGGER AS\n$body$\nBEGIN\n    RETURN [value];\nEND;\n$body$\nLANGUAGE plpgsql;\n\nCREATE TRIGGER [name]\n[BEFORE/AFTER/INSTEAD OF] [INSERT/UPDATE/DELETE]\nON [table]\nFOR EACH ROW\nEXECUTE PROCEDURE [tri_fun]();";

    public string DropTriggerTemplate(string name)
        => $"DROP TRIGGER IF EXISTS {name} ON [table_name]";
}
