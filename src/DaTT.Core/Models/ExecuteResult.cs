namespace DaTT.Core.Models;

public sealed record ExecuteResult(
    IReadOnlyList<ColumnMeta> Columns,
    IReadOnlyList<object?[]> Rows,
    int AffectedRows,
    TimeSpan ExecutionTime,
    string? Error = null
)
{
    public bool IsSuccess => Error is null;

    public static ExecuteResult FromRows(IReadOnlyList<ColumnMeta> columns, IReadOnlyList<object?[]> rows, TimeSpan elapsed)
        => new(columns, rows, rows.Count, elapsed);

    public static ExecuteResult FromAffected(int affectedRows, TimeSpan elapsed)
        => new([], [], affectedRows, elapsed);

    public static ExecuteResult FromError(string error, TimeSpan elapsed)
        => new([], [], 0, elapsed, error);

    public static ExecuteResult FromError(string error)
        => new([], [], 0, TimeSpan.Zero, error);
}
