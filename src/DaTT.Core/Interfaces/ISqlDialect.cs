using DaTT.Core.Models;

namespace DaTT.Core.Interfaces;

public interface ISqlDialect
{
    string ShowDatabases();
    string ShowSchemas();
    string ShowTables(string database);
    string ShowViews(string database);
    string ShowColumns(string database, string table);
    string ShowIndex(string database, string table);
    string ShowTriggers(string database);
    string ShowProcedures(string database);
    string ShowFunctions(string database);
    string ShowUsers();

    string ShowTableSource(string database, string table);
    string ShowViewSource(string database, string table);
    string ShowProcedureSource(string database, string name);
    string ShowFunctionSource(string database, string name);
    string ShowTriggerSource(string database, string name);

    string BuildPageSql(string database, string table, int pageSize);
    string CountSql(string database, string table);
    string PingDatabase(string database);

    string CreateDatabase(string database);
    string TruncateDatabase(string database);

    string AddColumn(string table);
    string UpdateColumn(UpdateColumnParam param);
    string UpdateTable(UpdateTableParam param);
    string CreateIndex(CreateIndexParam param);
    string DropIndex(string table, string indexName);

    string CreateUser();

    string ProcessList();
    string VariableList();
    string StatusList();

    string TableTemplate();
    string ViewTemplate();
    string ProcedureTemplate();
    string FunctionTemplate();
    string TriggerTemplate();
    string DropTriggerTemplate(string name);
}
