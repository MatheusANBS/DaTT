namespace DaTT.Core.Models;

public sealed record UpdateColumnParam(
    string Table,
    string ColumnName,
    string NewColumnName,
    string ColumnType,
    bool IsNullable,
    string? Comment = null
);
