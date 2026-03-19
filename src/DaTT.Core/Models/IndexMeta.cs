namespace DaTT.Core.Models;

public sealed record IndexMeta(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique,
    bool IsPrimaryKey,
    string? IndexType = null
);
