using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.App.Infrastructure;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.App.ViewModels;

public partial class QueryEditorTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;
    private CancellationTokenSource? _executionCts;
    private readonly HashSet<string> _knownTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownColumns = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SqlKeywords =
    [
        "select", "from", "where", "insert", "into", "values", "update", "set", "delete",
        "create", "alter", "drop", "truncate", "table", "view", "index", "constraint", "primary", "key",
        "join", "left", "right", "inner", "outer", "on", "group", "by", "order", "having", "limit",
        "offset", "union", "all", "distinct", "as", "and", "or", "not", "null", "is", "in", "exists",
        "count", "sum", "avg", "min", "max", "case", "when", "then", "else", "end"
    ];

    private static readonly string[] MultiWordKeywords =
    [
        "group by", "order by", "left join", "right join", "inner join", "outer join", "delete from"
    ];

    private const int MaxCompletionSuggestions = 20;

    public override string Title => "Query";

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private ExecuteResult? _queryResult;

    [ObservableProperty]
    private int _maxPreviewRows = 500;

    [ObservableProperty]
    private string? _resultInfoMessage;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private bool _isSuggestionsVisible;

    [ObservableProperty]
    private string? _languageToolStatusMessage;

    public ObservableCollection<string> QueryResultColumns { get; } = [];
    public ObservableCollection<GridRow> QueryResultRows { get; } = [];
    public ObservableCollection<string> QueryHistory { get; } = [];
    public ObservableCollection<QueryHistoryEntry> QueryHistoryEntries { get; } = [];
    public ObservableCollection<SqlOutlineItem> SqlOutlineItems { get; } = [];
    public ObservableCollection<string> CompletionSuggestions { get; } = [];
    public IReadOnlyList<int> PreviewRowOptions { get; } = [100, 250, 500, 1000, 5000];

    public QueryEditorTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    partial void OnQueryTextChanged(string value)
    {
        RefreshSqlOutline(value);
    }

    public async Task InitializeLanguageToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_knownTables.Count > 0 || _knownColumns.Count > 0)
            return;

        await RefreshLanguageToolsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshLanguageToolsAsync(CancellationToken cancellationToken = default)
    {
        LanguageToolStatusMessage = "Loading SQL metadata...";

        try
        {
            _knownTables.Clear();
            _knownColumns.Clear();

            var tables = await _provider.GetTablesAsync(cancellationToken);
            var views = await _provider.GetViewsAsync(cancellationToken);

            foreach (var table in tables)
                _knownTables.Add(table.Name);

            foreach (var view in views)
                _knownTables.Add(view.Name);

            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var columns = await _provider.GetColumnsAsync(table.Name, cancellationToken);
                    foreach (var column in columns)
                        _knownColumns.Add(column.Name);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Failed to load structure for '{table}': {ex.Message}");
                }
            }

            LanguageToolStatusMessage = $"Metadata loaded: {_knownTables.Count} objects, {_knownColumns.Count} columns.";
        }
        catch (OperationCanceledException)
        {
            LanguageToolStatusMessage = "Metadata load canceled.";
        }
        catch (Exception ex)
        {
            LanguageToolStatusMessage = "Failed to load metadata.";
            AppLog.Error("RefreshLanguageTools failed", ex);
        }
    }

    [RelayCommand]
    private void FormatSql()
    {
        if (string.IsNullOrWhiteSpace(QueryText))
            return;

        QueryText = FormatSqlScript(QueryText);
    }

    public async Task QueryTableAtCaretAsync(string script, int caretIndex, CancellationToken cancellationToken = default)
    {
        var identifier = ExtractIdentifierAtCaret(script, caretIndex);
        if (string.IsNullOrWhiteSpace(identifier) || !_knownTables.Contains(identifier))
        {
            LanguageToolStatusMessage = "Place the cursor on a known table/view name.";
            return;
        }

        var sql = $"SELECT * FROM {identifier} LIMIT 100;";
        await RunSingleSqlAsync(sql, cancellationToken);
    }

    public void UpdateCompletionSuggestions(string script, int caretIndex)
    {
        CompletionSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(script))
        {
            IsSuggestionsVisible = false;
            return;
        }

        var (start, _, prefix) = GetTokenReplacementRange(script, caretIndex);
        _ = start;

        var left = script[..Math.Clamp(caretIndex, 0, script.Length)];

        IEnumerable<string> source = SqlKeywords;
        if (IsTableContext(left))
            source = source.Concat(_knownTables);
        else if (IsColumnContext(left))
            source = source.Concat(_knownColumns);
        else
            source = source.Concat(_knownTables).Concat(_knownColumns);

        var ordered = source
            .Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCompletionSuggestions)
            .ToList();

        foreach (var item in ordered)
            CompletionSuggestions.Add(item);

        IsSuggestionsVisible = CompletionSuggestions.Count > 0;
    }

    public void RefreshSqlOutline(string script)
    {
        SqlOutlineItems.Clear();
        if (string.IsNullOrWhiteSpace(script))
            return;

        var statements = SplitSqlStatementsWithRanges(script);
        for (int i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var preview = Regex.Replace(statement.Sql, "\\s+", " ").Trim();
            if (preview.Length > 64)
                preview = preview[..64] + "...";

            var operation = statement.Sql.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? "SQL";
            SqlOutlineItems.Add(new SqlOutlineItem($"#{i + 1} {operation} - {preview}", statement.Start));
        }
    }

    [RelayCommand]
    private async Task RunQueryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;
        await RunSingleSqlAsync(QueryText, cancellationToken);
    }

    public async Task RunSingleSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        CancelExecution();
        _executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IsBusy = true;
        IsExecuting = true;
        ErrorMessage = null;
        ResultInfoMessage = null;

        try
        {
            AppLog.Info("RunSingleSql started");
            QueryResult = await _provider.ExecuteAsync(sql, _executionCts.Token);
            BindQueryResult(QueryResult);
            RememberQueryHistory(sql, QueryResult.ExecutionTime);
        }
        catch (OperationCanceledException)
        {
            ResultInfoMessage = "Execution canceled.";
            AppLog.Warn("RunSingleSql canceled by user");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("RunSingleSql failed", ex);
        }
        finally
        {
            IsBusy = false;
            IsExecuting = false;
            _executionCts?.Dispose();
            _executionCts = null;
        }
    }

    [RelayCommand]
    private async Task RunBatchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(QueryText)) return;

        var statements = SplitSqlStatements(QueryText);
        if (statements.Count == 0) return;

        CancelExecution();
        _executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IsBusy = true;
        IsExecuting = true;
        ErrorMessage = null;
        ResultInfoMessage = null;

        int succeeded = 0;
        int totalAffected = 0;
        ExecuteResult? lastQueryResult = null;

        try
        {
            AppLog.Info($"RunBatch started with {statements.Count} statements");
            foreach (var sql in statements)
            {
                _executionCts.Token.ThrowIfCancellationRequested();

                if (IsLikelyQuery(sql))
                {
                    lastQueryResult = await _provider.ExecuteAsync(sql, _executionCts.Token);
                    if (!lastQueryResult.IsSuccess)
                    {
                        ErrorMessage = lastQueryResult.Error;
                        return;
                    }
                }
                else
                {
                    var mutationResult = await _provider.ExecuteAsync(sql, _executionCts.Token);
                    totalAffected += mutationResult.AffectedRows;
                }

                succeeded++;
            }

            if (lastQueryResult is not null)
            {
                QueryResult = lastQueryResult;
                BindQueryResult(lastQueryResult);
            }
            else
            {
                QueryResultColumns.Clear();
                QueryResultRows.Clear();
            }

            ResultInfoMessage = $"Batch complete: {succeeded}/{statements.Count} statements, affected rows: {totalAffected}.";
            RememberQueryHistory(QueryText, lastQueryResult?.ExecutionTime);
        }
        catch (OperationCanceledException)
        {
            ResultInfoMessage = $"Batch canceled. Executed {succeeded}/{statements.Count} statements.";
            AppLog.Warn("RunBatch canceled by user");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("RunBatch failed", ex);
        }
        finally
        {
            IsBusy = false;
            IsExecuting = false;
            _executionCts?.Dispose();
            _executionCts = null;
        }
    }

    [RelayCommand]
    private void CancelExecution()
    {
        if (_executionCts is { IsCancellationRequested: false })
            _executionCts.Cancel();
    }

    private void BindQueryResult(ExecuteResult result)
    {
        QueryResultColumns.Clear();
        QueryResultRows.Clear();

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error;
            return;
        }

        foreach (var col in result.Columns)
            QueryResultColumns.Add(col.Name);

        var rowsToShow = result.Rows.Take(MaxPreviewRows).ToList();
        foreach (var row in rowsToShow)
            QueryResultRows.Add(new GridRow(row));

        ResultInfoMessage = result.Rows.Count > MaxPreviewRows
            ? $"Showing {MaxPreviewRows} of {result.Rows.Count} rows. Increase preview limit to view more."
            : $"Returned {result.Rows.Count} rows in {result.ExecutionTime.TotalMilliseconds:F0} ms.";
    }

    [RelayCommand]
    private void LoadHistoryEntry(QueryHistoryEntry? entry)
    {
        if (entry is null)
            return;

        QueryText = entry.Sql;
    }

    [RelayCommand]
    private async Task RunHistoryEntryAsync(QueryHistoryEntry? entry, CancellationToken cancellationToken = default)
    {
        if (entry is null)
            return;

        QueryText = entry.Sql;
        await RunSingleSqlAsync(entry.Sql, cancellationToken);
    }

    private void RememberQueryHistory(string sql, TimeSpan? elapsed)
    {
        var normalized = NormalizeSql(sql);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        var existingIndex = QueryHistory
            .Select((item, idx) => new { Item = item, Index = idx })
            .FirstOrDefault(x => NormalizeSql(x.Item) == normalized)
            ?.Index;

        if (existingIndex is int idx)
            QueryHistory.RemoveAt(idx);

        QueryHistory.Insert(0, sql.Trim());
        if (QueryHistory.Count > 50)
            QueryHistory.RemoveAt(50);

        var existingEntryIndex = QueryHistoryEntries
            .Select((item, idx) => new { Item = item, Index = idx })
            .FirstOrDefault(x => NormalizeSql(x.Item.Sql) == normalized)
            ?.Index;

        if (existingEntryIndex is int historyIdx)
            QueryHistoryEntries.RemoveAt(historyIdx);

        QueryHistoryEntries.Insert(0, new QueryHistoryEntry(
            Sql: sql.Trim(),
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            DurationMs: elapsed is null ? null : (long)Math.Round(elapsed.Value.TotalMilliseconds)));

        if (QueryHistoryEntries.Count > 100)
            QueryHistoryEntries.RemoveAt(100);
    }

    private static bool IsLikelyQuery(string sql)
    {
        var s = sql.TrimStart().ToLowerInvariant();
        return s.StartsWith("select") || s.StartsWith("show") || s.StartsWith("with") || s.StartsWith("desc") || s.StartsWith("describe");
    }

    private static List<string> SplitSqlStatements(string script)
        => SplitSqlStatementsWithRanges(script).Select(x => x.Sql).ToList();

    public static string? ExtractCurrentStatement(string script, int caretIndex)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        var ranges = SplitSqlStatementsWithRanges(script);
        if (ranges.Count == 0)
            return null;

        var safeCaret = Math.Clamp(caretIndex, 0, script.Length);
        var atCaret = ranges.FirstOrDefault(r => safeCaret >= r.Start && safeCaret <= r.End);
        if (!string.IsNullOrWhiteSpace(atCaret.Sql))
            return atCaret.Sql;

        var nearestPrevious = ranges.LastOrDefault(r => r.End <= safeCaret);
        return string.IsNullOrWhiteSpace(nearestPrevious.Sql) ? null : nearestPrevious.Sql;
    }

    private static List<(int Start, int End, string Sql)> SplitSqlStatementsWithRanges(string script)
    {
        var statements = new List<(int Start, int End, string Sql)>();
        var current = new System.Text.StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        int statementStart = 0;

        for (int i = 0; i < script.Length; i++)
        {
            var ch = script[i];

            if (ch == '\'' && !inDoubleQuote)
            {
                // SQL escaped single quote: '' should stay inside the same literal
                if (inSingleQuote && i + 1 < script.Length && script[i + 1] == '\'')
                {
                    current.Append(ch);
                    current.Append(script[i + 1]);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                // SQL escaped double quote for identifiers: ""
                if (inDoubleQuote && i + 1 < script.Length && script[i + 1] == '"')
                {
                    current.Append(ch);
                    current.Append(script[i + 1]);
                    i++;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
            }

            if (ch == ';' && !inSingleQuote && !inDoubleQuote)
            {
                var statement = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                    statements.Add((statementStart, i, statement));
                current.Clear();
                statementStart = i + 1;
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            statements.Add((statementStart, script.Length, tail));

        return statements;
    }

    private static string NormalizeSql(string sql)
    {
        var trimmed = sql.Trim();
        if (trimmed.Length == 0) return string.Empty;

        var compact = Regex.Replace(trimmed, "\\s+", " ");
        return compact;
    }

    public static (int Start, int End, string Prefix) GetTokenReplacementRange(string script, int caretIndex)
    {
        if (string.IsNullOrEmpty(script))
            return (0, 0, string.Empty);

        var safeCaret = Math.Clamp(caretIndex, 0, script.Length);
        var start = safeCaret;
        while (start > 0 && IsIdentifierChar(script[start - 1]))
            start--;

        var end = safeCaret;
        while (end < script.Length && IsIdentifierChar(script[end]))
            end++;

        var prefix = safeCaret > start ? script[start..safeCaret] : string.Empty;
        return (start, end, prefix);
    }

    public static string? ExtractIdentifierAtCaret(string script, int caretIndex)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        var (start, end, _) = GetTokenReplacementRange(script, caretIndex);
        if (end <= start)
            return null;

        return script[start..end].Trim();
    }

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is '_' or '.';

    private static bool IsTableContext(string leftScript)
        => Regex.IsMatch(leftScript, @"\b(from|join|update|into|table|truncate|delete\s+from)\s+[a-zA-Z0-9_\.]*$", RegexOptions.IgnoreCase);

    private static bool IsColumnContext(string leftScript)
        => Regex.IsMatch(leftScript, @"\b(select|where|and|or|on|set|having|group\s+by|order\s+by)\s+[a-zA-Z0-9_\.]*$", RegexOptions.IgnoreCase);

    private static string FormatSqlScript(string script)
    {
        var statements = SplitSqlStatements(script);
        if (statements.Count == 0)
            return script.Trim();

        var formattedStatements = statements.Select(FormatSqlStatement);
        return string.Join($";{Environment.NewLine}{Environment.NewLine}", formattedStatements) + ";";
    }

    private static string FormatSqlStatement(string sql)
    {
        var compact = Regex.Replace(sql.Trim(), "\\s+", " ");

        foreach (var keyword in MultiWordKeywords.OrderByDescending(k => k.Length))
        {
            compact = Regex.Replace(
                compact,
                $@"\b{Regex.Escape(keyword)}\b",
                keyword.ToUpperInvariant(),
                RegexOptions.IgnoreCase);
        }

        foreach (var keyword in SqlKeywords.OrderByDescending(k => k.Length))
        {
            compact = Regex.Replace(
                compact,
                $@"\b{Regex.Escape(keyword)}\b",
                keyword.ToUpperInvariant(),
                RegexOptions.IgnoreCase);
        }

        compact = Regex.Replace(
            compact,
            @"\s+(FROM|WHERE|GROUP BY|ORDER BY|HAVING|LIMIT|OFFSET|JOIN|LEFT JOIN|RIGHT JOIN|INNER JOIN|OUTER JOIN|VALUES|SET)\b",
            $"{Environment.NewLine}$1",
            RegexOptions.IgnoreCase);

        var lines = compact.Split(Environment.NewLine, StringSplitOptions.None)
            .Select((line, index) => index == 0 ? line.Trim() : "    " + line.Trim())
            .ToArray();

        var builder = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                builder.AppendLine();
            builder.Append(lines[i]);
        }

        return builder.ToString();
    }
}

public sealed record SqlOutlineItem(string Label, int StartIndex);

public sealed record QueryHistoryEntry(string Sql, DateTimeOffset ExecutedAtUtc, long? DurationMs)
{
    public string SqlPreview
    {
        get
        {
            var normalized = Regex.Replace(Sql.Trim(), "\\s+", " ");
            return normalized.Length > 72 ? normalized[..72] + "..." : normalized;
        }
    }

    public string DisplayTimestamp => ExecutedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DisplayDuration => DurationMs is null ? "n/a" : $"{DurationMs.Value} ms";
}
