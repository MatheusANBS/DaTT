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

    private string BareType => (DataType ?? string.Empty).ToLowerInvariant().Split('(')[0].Trim();

    public bool IsDateTimeType => BareType == "datetime" || BareType.StartsWith("timestamp");
    public bool IsDateOnlyType => BareType == "date";
    public bool IsTimeOnlyType => (BareType == "time" || BareType == "timetz") && !IsDateTimeType;
    public bool IsAnyDateTimeType => IsDateTimeType || IsDateOnlyType || IsTimeOnlyType;
}
