using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.Providers.Dialects;

public class MysqlDialect : ISqlDialect
{
    public string ShowDatabases() => "SHOW DATABASES";

    public string ShowSchemas() => "SHOW DATABASES";

    public string ShowTables(string database)
        => $"SELECT TABLE_NAME AS `name`, TABLE_COMMENT AS `comment`, TABLE_ROWS AS `rows`, AUTO_INCREMENT, ROW_FORMAT, DATA_LENGTH, INDEX_LENGTH FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{database}' AND TABLE_TYPE <> 'VIEW' ORDER BY TABLE_NAME";

    public string ShowViews(string database)
        => $"SELECT TABLE_NAME AS `name` FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = '{database}'";

    public string ShowColumns(string database, string table)
        => $"SELECT COLUMN_NAME name, DATA_TYPE simpleType, COLUMN_TYPE type, COLUMN_COMMENT comment, COLUMN_KEY `key`, IS_NULLABLE nullable, CHARACTER_MAXIMUM_LENGTH maxLength, COLUMN_DEFAULT defaultValue, EXTRA extra FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{database}' AND TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION";

    public string ShowIndex(string database, string table)
        => $"SELECT COLUMN_NAME, INDEX_NAME, NON_UNIQUE, INDEX_TYPE FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = '{database}' AND TABLE_NAME = '{table}'";

    public string ShowTriggers(string database)
        => $"SELECT TRIGGER_NAME FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_SCHEMA = '{database}'";

    public string ShowProcedures(string database)
        => $"SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = '{database}' AND ROUTINE_TYPE = 'PROCEDURE'";

    public string ShowFunctions(string database)
        => $"SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA = '{database}' AND ROUTINE_TYPE = 'FUNCTION'";

    public string ShowUsers()
        => "SELECT CONCAT(user, '@', host) AS user FROM mysql.user";

    public string ShowTableSource(string database, string table)
        => $"SHOW CREATE TABLE `{database}`.`{table}`";

    public string ShowViewSource(string database, string table)
        => $"SHOW CREATE VIEW `{database}`.`{table}`";

    public string ShowProcedureSource(string database, string name)
        => $"SHOW CREATE PROCEDURE `{database}`.`{name}`";

    public string ShowFunctionSource(string database, string name)
        => $"SHOW CREATE FUNCTION `{database}`.`{name}`";

    public string ShowTriggerSource(string database, string name)
        => $"SHOW CREATE TRIGGER `{database}`.`{name}`";

    public string BuildPageSql(string database, string table, int pageSize)
        => $"SELECT * FROM `{table}` LIMIT {pageSize}";

    public string CountSql(string database, string table)
        => $"SELECT COUNT(*) FROM `{table}`";

    public string PingDatabase(string database)
        => string.IsNullOrEmpty(database) ? "SELECT 1" : $"USE `{database}`";

    public string CreateDatabase(string database)
        => $"CREATE DATABASE `{database}` DEFAULT CHARACTER SET = 'utf8mb4'";

    public string TruncateDatabase(string database)
        => $"SELECT CONCAT('TRUNCATE TABLE `', TABLE_SCHEMA, '`.`', TABLE_NAME, '`;') AS trun FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{database}' AND TABLE_TYPE <> 'VIEW'";

    public string AddColumn(string table)
        => $"ALTER TABLE {table} ADD COLUMN [column] [type] NOT NULL COMMENT ''";

    public string UpdateColumn(UpdateColumnParam param)
    {
        var nullability = param.IsNullable ? "" : " NOT NULL";
        var comment = string.IsNullOrEmpty(param.Comment) ? "" : $" COMMENT '{param.Comment}'";
        return $"ALTER TABLE {param.Table} CHANGE {param.ColumnName} {param.NewColumnName} {param.ColumnType}{nullability}{comment}";
    }

    public string UpdateTable(UpdateTableParam param)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(param.NewComment) && param.NewComment != param.Comment)
            parts.Add($"ALTER TABLE {param.Table} COMMENT = '{param.NewComment}'");
        if (!string.IsNullOrEmpty(param.NewTableName) && param.NewTableName != param.Table)
            parts.Add($"ALTER TABLE {param.Table} RENAME TO {param.NewTableName}");
        return string.Join(";\n", parts);
    }

    public string CreateIndex(CreateIndexParam param)
    {
        var unique = param.IsUnique ? "UNIQUE " : "";
        return $"ALTER TABLE {param.Table} ADD {unique}INDEX ({param.Column})";
    }

    public string DropIndex(string table, string indexName)
        => $"ALTER TABLE {table} DROP INDEX {indexName}";

    public string CreateUser()
        => "CREATE USER 'username'@'%' IDENTIFIED BY 'password'";

    public string ProcessList() => "SHOW PROCESSLIST";
    public string VariableList() => "SHOW GLOBAL VARIABLES";
    public string StatusList() => "SHOW GLOBAL STATUS";

    public string TableTemplate()
        => """
           CREATE TABLE [name](
               id INT NOT NULL PRIMARY KEY AUTO_INCREMENT COMMENT 'primary key',
               create_time DATETIME COMMENT 'create time',
               update_time DATETIME COMMENT 'update time',
               [column] VARCHAR(255) COMMENT ''
           ) DEFAULT CHARSET utf8mb4 COMMENT '';
           """;

    public string ViewTemplate()
        => "CREATE VIEW [name]\nAS\n(SELECT * FROM ...)";

    public string ProcedureTemplate()
        => "CREATE PROCEDURE [name]()\nBEGIN\n\nEND;";

    public string FunctionTemplate()
        => "CREATE FUNCTION [name]() RETURNS [TYPE]\nBEGIN\n    RETURN [value];\nEND;";

    public string TriggerTemplate()
        => "CREATE TRIGGER [name]\n[BEFORE/AFTER] [INSERT/UPDATE/DELETE]\nON [table]\nFOR EACH ROW\nBEGIN\n\nEND;";

    public string DropTriggerTemplate(string name)
        => $"DROP TRIGGER IF EXISTS {name}";
}
