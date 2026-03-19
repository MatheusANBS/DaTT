using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.Providers.Dialects;

public class OracleDialect : ISqlDialect
{
    public string ShowDatabases()
        => "SELECT name AS \"Database\" FROM v$database";

    public string ShowSchemas()
        => "SELECT username AS \"schema\" FROM all_users ORDER BY username";

    public string ShowTables(string database)
        => "SELECT TABLE_NAME AS \"name\" FROM USER_TABLES ORDER BY TABLE_NAME";

    public string ShowViews(string database)
        => "SELECT VIEW_NAME AS \"name\" FROM USER_VIEWS ORDER BY VIEW_NAME";

    public string ShowColumns(string database, string table)
        => $"SELECT c.COLUMN_NAME AS \"name\", c.DATA_TYPE AS \"simpleType\", c.DATA_TYPE AS \"type\", c.NULLABLE AS \"nullable\", c.DATA_LENGTH AS \"maxLength\", c.DATA_DEFAULT AS \"defaultValue\", '' AS \"comment\", NVL2(p.COLUMN_NAME, 'PRIMARY KEY', '') AS \"key\" FROM USER_TAB_COLUMNS c LEFT JOIN (SELECT cc.COLUMN_NAME FROM USER_CONSTRAINTS uc JOIN USER_CONS_COLUMNS cc ON uc.CONSTRAINT_NAME = cc.CONSTRAINT_NAME WHERE uc.CONSTRAINT_TYPE = 'P' AND uc.TABLE_NAME = '{table.ToUpperInvariant()}') p ON c.COLUMN_NAME = p.COLUMN_NAME WHERE c.TABLE_NAME = '{table.ToUpperInvariant()}' ORDER BY c.COLUMN_ID";

    public string ShowIndex(string database, string table)
        => $"SELECT i.INDEX_NAME, ic.COLUMN_NAME, i.UNIQUENESS FROM USER_INDEXES i JOIN USER_IND_COLUMNS ic ON i.INDEX_NAME = ic.INDEX_NAME WHERE i.TABLE_NAME = '{table.ToUpperInvariant()}' ORDER BY i.INDEX_NAME, ic.COLUMN_POSITION";

    public string ShowTriggers(string database)
        => "SELECT TRIGGER_NAME FROM USER_TRIGGERS ORDER BY TRIGGER_NAME";

    public string ShowProcedures(string database)
        => "SELECT OBJECT_NAME AS ROUTINE_NAME FROM USER_PROCEDURES WHERE OBJECT_TYPE = 'PROCEDURE' ORDER BY OBJECT_NAME";

    public string ShowFunctions(string database)
        => "SELECT OBJECT_NAME AS ROUTINE_NAME FROM USER_PROCEDURES WHERE OBJECT_TYPE = 'FUNCTION' ORDER BY OBJECT_NAME";

    public string ShowUsers()
        => "SELECT username AS \"user\" FROM all_users ORDER BY username";

    public string ShowTableSource(string database, string table)
        => $"SELECT DBMS_METADATA.GET_DDL('TABLE', '{table.ToUpperInvariant()}') AS \"Create Table\" FROM DUAL";

    public string ShowViewSource(string database, string table)
        => $"SELECT TEXT AS \"Create View\" FROM USER_VIEWS WHERE VIEW_NAME = '{table.ToUpperInvariant()}'";

    public string ShowProcedureSource(string database, string name)
        => $"SELECT TEXT AS \"Create Procedure\" FROM USER_SOURCE WHERE NAME = '{name.ToUpperInvariant()}' AND TYPE = 'PROCEDURE' ORDER BY LINE";

    public string ShowFunctionSource(string database, string name)
        => $"SELECT TEXT AS \"Create Function\" FROM USER_SOURCE WHERE NAME = '{name.ToUpperInvariant()}' AND TYPE = 'FUNCTION' ORDER BY LINE";

    public string ShowTriggerSource(string database, string name)
        => $"SELECT TRIGGER_BODY AS \"SQL Original Statement\" FROM USER_TRIGGERS WHERE TRIGGER_NAME = '{name.ToUpperInvariant()}'";

    public string BuildPageSql(string database, string table, int pageSize)
        => $"SELECT * FROM (SELECT a.*, ROWNUM rnum FROM {table} a WHERE ROWNUM <= {pageSize}) WHERE rnum > 0";

    public string CountSql(string database, string table)
        => $"SELECT COUNT(*) FROM {table}";

    public string PingDatabase(string database)
        => "SELECT 1 FROM DUAL";

    public string CreateDatabase(string database)
        => $"CREATE USER \"{database}\" IDENTIFIED BY \"password\"";

    public string TruncateDatabase(string database)
        => "SELECT 'TRUNCATE TABLE ' || TABLE_NAME || ';' AS trun FROM USER_TABLES";

    public string AddColumn(string table)
        => $"ALTER TABLE {table} ADD ([column] [type])";

    public string UpdateColumn(UpdateColumnParam param)
    {
        var sql = $"ALTER TABLE {param.Table} MODIFY ({param.ColumnName} {param.ColumnType}";
        sql += param.IsNullable ? " NULL" : " NOT NULL";
        sql += ")";
        if (param.ColumnName != param.NewColumnName)
            sql += $";\nALTER TABLE {param.Table} RENAME COLUMN {param.ColumnName} TO {param.NewColumnName}";
        if (!string.IsNullOrEmpty(param.Comment))
            sql += $";\nCOMMENT ON COLUMN {param.Table}.{param.ColumnName} IS '{param.Comment}'";
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
        var unique = param.IsUnique ? "UNIQUE " : "";
        return $"CREATE {unique}INDEX idx_{param.Column} ON {param.Table} ({param.Column})";
    }

    public string DropIndex(string table, string indexName)
        => $"DROP INDEX {indexName}";

    public string CreateUser()
        => "CREATE USER [name] IDENTIFIED BY 'password'";

    public string ProcessList()
        => "SELECT s.SID AS \"Id\", s.USERNAME AS \"User\", s.MACHINE AS \"Host\", s.STATUS AS \"State\", s.SQL_ID AS \"Command\", s.LOGON_TIME AS \"Time\" FROM v$session s WHERE s.TYPE = 'USER'";

    public string VariableList()
        => "SELECT name, value FROM v$parameter ORDER BY name";

    public string StatusList()
        => "SELECT name, value FROM v$sysstat WHERE ROWNUM <= 50";

    public string TableTemplate()
        => """
           CREATE TABLE [name](
               id NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
               create_time DATE,
               update_time DATE,
               [column] VARCHAR2(255)
           );
           """;

    public string ViewTemplate()
        => "CREATE VIEW [name] AS\nSELECT * FROM ...";

    public string ProcedureTemplate()
        => "CREATE OR REPLACE PROCEDURE [name]\nIS\nBEGIN\n    NULL;\nEND;";

    public string FunctionTemplate()
        => "CREATE OR REPLACE FUNCTION [name]\nRETURN [type]\nIS\nBEGIN\n    RETURN [value];\nEND;";

    public string TriggerTemplate()
        => "CREATE OR REPLACE TRIGGER [name]\n[BEFORE/AFTER] [INSERT/UPDATE/DELETE]\nON [table]\nFOR EACH ROW\nBEGIN\n    NULL;\nEND;";

    public string DropTriggerTemplate(string name)
        => $"DROP TRIGGER {name}";
}
