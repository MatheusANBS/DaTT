namespace DaTT.Core.Models;

public sealed record ForeignKeyMeta(
    string Name,
    string SourceColumn,
    string ReferencedTable,
    string ReferencedColumn
);
