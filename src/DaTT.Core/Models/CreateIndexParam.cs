namespace DaTT.Core.Models;

public sealed record CreateIndexParam(
    string Table,
    string Column,
    string? IndexType = null,
    bool IsUnique = false
);
