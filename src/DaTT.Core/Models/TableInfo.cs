namespace DaTT.Core.Models;

public sealed record TableInfo(
    string Name,
    string? Comment = null,
    long? RowCount = null,
    string? Engine = null
);
