namespace DaTT.Core.Models;

public sealed record SchemaDiffItem(
    string Kind,
    string ObjectName,
    string Description,
    string Sql
);
