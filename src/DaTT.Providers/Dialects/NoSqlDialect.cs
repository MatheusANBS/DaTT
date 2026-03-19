using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.Providers.Dialects;

public sealed class NoSqlDialect : ISqlDialect
{
    public static readonly NoSqlDialect Instance = new();

    public string ShowDatabases() => string.Empty;
    public string ShowSchemas() => string.Empty;
    public string ShowTables(string database) => string.Empty;
    public string ShowViews(string database) => string.Empty;
    public string ShowColumns(string database, string table) => string.Empty;
    public string ShowIndex(string database, string table) => string.Empty;
    public string ShowTriggers(string database) => string.Empty;
    public string ShowProcedures(string database) => string.Empty;
    public string ShowFunctions(string database) => string.Empty;
    public string ShowUsers() => string.Empty;
    public string ShowTableSource(string database, string table) => string.Empty;
    public string ShowViewSource(string database, string table) => string.Empty;
    public string ShowProcedureSource(string database, string name) => string.Empty;
    public string ShowFunctionSource(string database, string name) => string.Empty;
    public string ShowTriggerSource(string database, string name) => string.Empty;
    public string BuildPageSql(string database, string table, int pageSize) => string.Empty;
    public string CountSql(string database, string table) => string.Empty;
    public string PingDatabase(string database) => string.Empty;
    public string CreateDatabase(string database) => string.Empty;
    public string TruncateDatabase(string database) => string.Empty;
    public string AddColumn(string table) => string.Empty;
    public string UpdateColumn(UpdateColumnParam param) => string.Empty;
    public string UpdateTable(UpdateTableParam param) => string.Empty;
    public string CreateIndex(CreateIndexParam param) => string.Empty;
    public string DropIndex(string table, string indexName) => string.Empty;
    public string CreateUser() => string.Empty;
    public string ProcessList() => string.Empty;
    public string VariableList() => string.Empty;
    public string StatusList() => string.Empty;
    public string TableTemplate() => string.Empty;
    public string ViewTemplate() => string.Empty;
    public string ProcedureTemplate() => string.Empty;
    public string FunctionTemplate() => string.Empty;
    public string TriggerTemplate() => string.Empty;
    public string DropTriggerTemplate(string name) => string.Empty;
}
