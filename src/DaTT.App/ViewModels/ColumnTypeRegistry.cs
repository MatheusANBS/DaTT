namespace DaTT.App.ViewModels;

// #region Types
public static class ColumnTypeRegistry
{
    private static readonly IReadOnlyList<string> PostgreSqlTypes =
    [
        "INTEGER", "BIGINT", "SMALLINT", "SERIAL", "BIGSERIAL",
        "REAL", "DOUBLE PRECISION", "NUMERIC", "DECIMAL",
        "VARCHAR", "CHAR", "TEXT",
        "BOOLEAN",
        "DATE", "TIME", "TIMESTAMP", "TIMESTAMPTZ",
        "JSON", "JSONB", "UUID", "BYTEA"
    ];

    private static readonly IReadOnlyList<string> MySqlTypes =
    [
        "INT", "BIGINT", "SMALLINT", "TINYINT", "SERIAL",
        "FLOAT", "DOUBLE", "DECIMAL",
        "VARCHAR", "CHAR", "TEXT", "MEDIUMTEXT", "LONGTEXT",
        "BOOL",
        "DATE", "TIME", "DATETIME", "TIMESTAMP",
        "JSON", "BLOB", "LONGBLOB", "ENUM"
    ];

    private static readonly IReadOnlyList<string> OracleTypes =
    [
        "NUMBER", "INTEGER", "BINARY_FLOAT", "BINARY_DOUBLE",
        "VARCHAR2", "CHAR", "NVARCHAR2", "CLOB", "NCLOB",
        "BLOB", "RAW",
        "DATE", "TIMESTAMP", "INTERVAL YEAR TO MONTH", "INTERVAL DAY TO SECOND",
        "XMLTYPE"
    ];

    private static readonly IReadOnlyList<string> HiveTypes =
    [
        "STRING", "INT", "BIGINT", "SMALLINT", "TINYINT",
        "FLOAT", "DOUBLE", "DECIMAL",
        "BOOLEAN",
        "DATE", "TIMESTAMP",
        "BINARY", "ARRAY", "MAP", "STRUCT"
    ];

    private static readonly IReadOnlyList<string> DefaultTypes =
    [
        "INTEGER", "REAL", "TEXT", "BLOB", "NUMERIC"
    ];

    private static readonly IReadOnlySet<string> TypesWithLength = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VARCHAR", "CHAR", "VARCHAR2", "NVARCHAR2"
    };

    private static readonly IReadOnlySet<string> TypesWithPrecision = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NUMERIC", "DECIMAL", "NUMBER"
    };

    public static IReadOnlyList<string> GetTypes(string engineName) =>
        engineName switch
        {
            var e when e.Contains("postgresql", StringComparison.OrdinalIgnoreCase)
                    || e.Contains("postgres", StringComparison.OrdinalIgnoreCase) => PostgreSqlTypes,
            var e when e.Contains("mysql", StringComparison.OrdinalIgnoreCase)
                    || e.Contains("mariadb", StringComparison.OrdinalIgnoreCase) => MySqlTypes,
            var e when e.Contains("oracle", StringComparison.OrdinalIgnoreCase) => OracleTypes,
            var e when e.Contains("hive", StringComparison.OrdinalIgnoreCase) => HiveTypes,
            _ => DefaultTypes
        };

    public static bool NeedsLength(string typeName) =>
        TypesWithLength.Contains(typeName.Split('(')[0].Trim());

    public static bool NeedsPrecision(string typeName) =>
        TypesWithPrecision.Contains(typeName.Split('(')[0].Trim());
}
// #endregion Types
