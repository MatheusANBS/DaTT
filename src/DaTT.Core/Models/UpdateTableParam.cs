namespace DaTT.Core.Models;

public sealed record UpdateTableParam(
    string Table,
    string? NewTableName = null,
    string? Comment = null,
    string? NewComment = null
);
