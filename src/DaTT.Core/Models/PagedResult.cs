namespace DaTT.Core.Models;

public sealed record PagedResult<T>(
    T Data,
    int TotalRows,
    int Page,
    int PageSize
)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRows / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
