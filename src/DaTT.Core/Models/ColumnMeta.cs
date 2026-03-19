namespace DaTT.Core.Models;

public sealed record ColumnMeta(
    string Name,
    string DataType,
    string? SimpleType = null,
    string? Comment = null,
    string? Key = null,
    bool IsNullable = true,
    int? MaxLength = null,
    string? DefaultValue = null,
    string? Extra = null,
    string? OrgTable = null,
    int OrdinalPosition = 0
)
{
    public bool IsPrimaryKey => string.Equals(Key, "PRI", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(Key, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase);
}
