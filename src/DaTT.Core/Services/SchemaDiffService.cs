using DaTT.Core.Models;

namespace DaTT.Core.Services;

public static class SchemaDiffService
{
    public static SchemaDiffPlan BuildPlan(
        string tableName,
        IReadOnlyList<ColumnMeta> sourceColumns,
        IReadOnlyList<IndexMeta> sourceIndexes,
        IReadOnlyList<ColumnMeta> destinationColumns,
        IReadOnlyList<IndexMeta> destinationIndexes,
        string engineName,
        bool includeDrops = false)
    {
        var items = new List<SchemaDiffItem>();

        var srcColMap = sourceColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var dstColMap = destinationColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceColumn in sourceColumns.OrderBy(c => c.OrdinalPosition))
        {
            if (!dstColMap.TryGetValue(sourceColumn.Name, out var destinationColumn))
            {
                items.Add(new SchemaDiffItem(
                    Kind: "AddColumn",
                    ObjectName: sourceColumn.Name,
                    Description: $"Column '{sourceColumn.Name}' is missing in destination.",
                    Sql: BuildAddColumnSql(tableName, sourceColumn, engineName)));
                continue;
            }

            var mismatchReason = GetColumnMismatchReason(sourceColumn, destinationColumn);
            if (mismatchReason is not null)
            {
                items.Add(new SchemaDiffItem(
                    Kind: "AlterColumn",
                    ObjectName: sourceColumn.Name,
                    Description: mismatchReason,
                    Sql: BuildAlterColumnSql(tableName, sourceColumn, engineName)));
            }
        }

        if (includeDrops)
        {
            foreach (var destinationColumn in destinationColumns)
            {
                if (!srcColMap.ContainsKey(destinationColumn.Name))
                {
                    items.Add(new SchemaDiffItem(
                        Kind: "DropColumn",
                        ObjectName: destinationColumn.Name,
                        Description: $"Column '{destinationColumn.Name}' exists only in destination.",
                        Sql: BuildDropColumnSql(tableName, destinationColumn.Name, engineName)));
                }
            }
        }

        var srcIdxMap = sourceIndexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var dstIdxMap = destinationIndexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceIndex in sourceIndexes.Where(i => !i.IsPrimaryKey))
        {
            if (!dstIdxMap.TryGetValue(sourceIndex.Name, out var destinationIndex))
            {
                items.Add(new SchemaDiffItem(
                    Kind: "CreateIndex",
                    ObjectName: sourceIndex.Name,
                    Description: $"Index '{sourceIndex.Name}' is missing in destination.",
                    Sql: BuildCreateIndexSql(tableName, sourceIndex, engineName)));
                continue;
            }

            if (!AreEquivalentIndexes(sourceIndex, destinationIndex))
            {
                items.Add(new SchemaDiffItem(
                    Kind: "RecreateIndex",
                    ObjectName: sourceIndex.Name,
                    Description: $"Index '{sourceIndex.Name}' differs between source and destination.",
                    Sql: BuildDropCreateIndexSql(tableName, sourceIndex, engineName)));
            }
        }

        if (includeDrops)
        {
            foreach (var destinationIndex in destinationIndexes.Where(i => !i.IsPrimaryKey))
            {
                if (!srcIdxMap.ContainsKey(destinationIndex.Name))
                {
                    items.Add(new SchemaDiffItem(
                        Kind: "DropIndex",
                        ObjectName: destinationIndex.Name,
                        Description: $"Index '{destinationIndex.Name}' exists only in destination.",
                        Sql: BuildDropIndexSql(tableName, destinationIndex.Name, engineName)));
                }
            }
        }

        return new SchemaDiffPlan(items);
    }

    private static string? GetColumnMismatchReason(ColumnMeta source, ColumnMeta destination)
    {
        if (!string.Equals(source.DataType, destination.DataType, StringComparison.OrdinalIgnoreCase))
            return $"Column '{source.Name}' type differs: source '{source.DataType}', destination '{destination.DataType}'.";

        if (source.IsNullable != destination.IsNullable)
            return $"Column '{source.Name}' nullable differs: source '{source.IsNullable}', destination '{destination.IsNullable}'.";

        var sourceDefault = NormalizeDefault(source.DefaultValue);
        var destinationDefault = NormalizeDefault(destination.DefaultValue);
        if (!string.Equals(sourceDefault, destinationDefault, StringComparison.OrdinalIgnoreCase))
            return $"Column '{source.Name}' default value differs.";

        return null;
    }

    private static string NormalizeDefault(string? value)
        => (value ?? string.Empty).Trim();

    private static bool AreEquivalentIndexes(IndexMeta source, IndexMeta destination)
    {
        if (source.IsUnique != destination.IsUnique)
            return false;

        if (source.Columns.Count != destination.Columns.Count)
            return false;

        for (int i = 0; i < source.Columns.Count; i++)
        {
            if (!string.Equals(source.Columns[i], destination.Columns[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string BuildAddColumnSql(string tableName, ColumnMeta column, string engineName)
    {
        var type = column.DataType;
        var nullable = column.IsNullable ? string.Empty : " NOT NULL";
        var defaultValue = string.IsNullOrWhiteSpace(column.DefaultValue) ? string.Empty : $" DEFAULT {column.DefaultValue}";
        return $"ALTER TABLE {QuoteIdentifier(tableName, engineName)} ADD COLUMN {QuoteIdentifier(column.Name, engineName)} {type}{nullable}{defaultValue};";
    }

    private static string BuildAlterColumnSql(string tableName, ColumnMeta column, string engineName)
    {
        var type = column.DataType;
        var nullable = column.IsNullable ? string.Empty : " NOT NULL";
        var defaultValue = string.IsNullOrWhiteSpace(column.DefaultValue) ? string.Empty : $" DEFAULT {column.DefaultValue}";

        if (IsMySqlLike(engineName))
        {
            return $"ALTER TABLE {QuoteIdentifier(tableName, engineName)} MODIFY COLUMN {QuoteIdentifier(column.Name, engineName)} {type}{nullable}{defaultValue};";
        }

        return $"ALTER TABLE {QuoteIdentifier(tableName, engineName)} ALTER COLUMN {QuoteIdentifier(column.Name, engineName)} TYPE {type};";
    }

    private static string BuildDropColumnSql(string tableName, string columnName, string engineName)
        => $"ALTER TABLE {QuoteIdentifier(tableName, engineName)} DROP COLUMN {QuoteIdentifier(columnName, engineName)};";

    private static string BuildCreateIndexSql(string tableName, IndexMeta index, string engineName)
    {
        var uniquePart = index.IsUnique ? "UNIQUE " : string.Empty;
        var columns = string.Join(", ", index.Columns.Select(c => QuoteIdentifier(c, engineName)));
        return $"CREATE {uniquePart}INDEX {QuoteIdentifier(index.Name, engineName)} ON {QuoteIdentifier(tableName, engineName)} ({columns});";
    }

    private static string BuildDropCreateIndexSql(string tableName, IndexMeta sourceIndex, string engineName)
        => BuildDropIndexSql(tableName, sourceIndex.Name, engineName) + Environment.NewLine + BuildCreateIndexSql(tableName, sourceIndex, engineName);

    private static string BuildDropIndexSql(string tableName, string indexName, string engineName)
    {
        if (IsMySqlLike(engineName))
            return $"DROP INDEX {QuoteIdentifier(indexName, engineName)} ON {QuoteIdentifier(tableName, engineName)};";

        return $"DROP INDEX {QuoteIdentifier(indexName, engineName)};";
    }

    private static bool IsMySqlLike(string engineName)
        => engineName.Contains("mysql", StringComparison.OrdinalIgnoreCase)
           || engineName.Contains("maria", StringComparison.OrdinalIgnoreCase);

    private static string QuoteIdentifier(string identifier, string engineName)
    {
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return identifier;

        if (IsMySqlLike(engineName))
            return string.Join('.', parts.Select(static part => $"`{part.Replace("`", "``")}`"));

        return string.Join('.', parts.Select(static part => $"\"{part.Replace("\"", "\"\"")}\""));
    }
}
