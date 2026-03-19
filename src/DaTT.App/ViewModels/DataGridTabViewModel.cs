using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClosedXML.Excel;
using DaTT.App.Infrastructure;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.App.ViewModels;

public partial class DataGridTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;
    private readonly string _tableName;
    private IReadOnlyList<ColumnMeta> _columnInfos = [];
    private readonly Dictionary<(GridRow Row, int ColumnIndex), string?> _pendingCellEdits = [];

    private const int DefaultPageSize = 100;

    public override string Title => _tableName;

    public ObservableCollection<string> ColumnNames { get; } = [];
    public ObservableCollection<GridRow> Rows { get; } = [];

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalRows;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private GridRow? _selectedRow;
    [ObservableProperty] private bool _isInlineEditMode;
    [ObservableProperty] private string _inlineEditStatus = "Double-click a cell to enable inline editing.";
    [ObservableProperty] private int _pendingChangeCount;
    [ObservableProperty] private bool _exportFullTable = true;
    [ObservableProperty] private string _selectedExportFormat = "csv";

    public bool IsGridReadOnly => !IsInlineEditMode;
    public bool HasPendingChanges => PendingChangeCount > 0;
    public bool IsInlineStatusVisible => IsInlineEditMode || HasPendingChanges;
    public IReadOnlyList<string> ExportFormats { get; } = ["csv", "json", "sql", "xlsx"];
    public string TableName => _tableName;
    public string EngineName => _provider.EngineName;

    // Delegates set by the View to show dialogs — plain Func<> so the View can assign directly
    public Func<EditRowViewModel, Task>? ShowEditDialog { get; set; }
    public Func<CellExpandViewModel, Task>? ShowExpandDialog { get; set; }
    public Func<CellEditViewModel, Task>? ShowCellEditDialog { get; set; }
    public Func<string, Task<bool>>? ShowConfirmDialog { get; set; }
    public Func<string, Task<string?>>? PickExportPath { get; set; }
    public Func<Task<string?>>? PickImportPath { get; set; }

    public DataGridTabViewModel(IDatabaseProvider provider, string tableName)
    {
        _provider = provider;
        _tableName = tableName;
    }

    partial void OnIsInlineEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGridReadOnly));
        OnPropertyChanged(nameof(IsInlineStatusVisible));
        if (!value)
            InlineEditStatus = "Inline editing disabled.";
    }

    partial void OnPendingChangeCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(IsInlineStatusVisible));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info($"Loading table '{_tableName}'");
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            AppLog.Info($"Fetching columns for '{_tableName}'");
            _columnInfos = await _provider.GetColumnsAsync(_tableName, cancellationToken);
            AppLog.Info($"Structure OK — {_columnInfos.Count} columns");

            ColumnNames.Clear();
            foreach (var col in _columnInfos)
                ColumnNames.Add(col.Name);

            await LoadPageAsync(1, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Error($"LoadAsync failed for '{_tableName}'", ex);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Paging ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task NextPageAsync() => CurrentPage < TotalPages
        ? LoadPageAsync(CurrentPage + 1) : Task.CompletedTask;

    [RelayCommand]
    private Task PreviousPageAsync() => CurrentPage > 1
        ? LoadPageAsync(CurrentPage - 1) : Task.CompletedTask;

    [RelayCommand]
    private Task ApplyFilterAsync() => LoadPageAsync(1);

    private async Task LoadPageAsync(int page, CancellationToken cancellationToken = default)
    {
        AppLog.Info($"Loading page {page} of '{_tableName}'");
        IsBusy = true;
        try
        {
            var result = await _provider.GetRowsAsync(
                _tableName, page, DefaultPageSize,
                string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
                ct: cancellationToken);

            AppLog.Info($"Page {page} OK — {result.Data.Count} rows (total {result.TotalRows})");

            Rows.Clear();
            foreach (var row in result.Data)
            {
                var gridRow = new GridRow(row);
                gridRow.CellEdited += OnRowCellEdited;
                Rows.Add(gridRow);
            }

            _pendingCellEdits.Clear();
            PendingChangeCount = 0;

            CurrentPage = result.Page;
            TotalPages = result.TotalPages;
            TotalRows = result.TotalRows;
        }
        catch (Exception ex)
        {
            AppLog.Error($"LoadPageAsync failed for '{_tableName}' page {page}", ex);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Row context actions ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task EditRowAsync(GridRow row)
    {
        var vm = BuildEditViewModel("Edit", row);
        if (ShowEditDialog is not null)
            await ShowEditDialog(vm);

        if (!vm.Confirmed) return;

        try
        {
            var pkValues = GetPkValues(row);
            var newValues = ConvertEditFields(vm.Fields);
            await _provider.UpdateRowAsync(_tableName, newValues, pkValues);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task InsertRowAsync()
    {
        var vm = BuildEditViewModel("Insert", null);
        if (ShowEditDialog is not null)
            await ShowEditDialog(vm);

        if (!vm.Confirmed) return;

        try
        {
            await _provider.InsertRowAsync(_tableName, ConvertEditFields(vm.Fields));
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DuplicateRowAsync(GridRow row)
    {
        // Build insert values from existing row but strip PK columns so DB auto-assigns
        var values = new Dictionary<string, object?>();
        for (int i = 0; i < _columnInfos.Count && i < row.Cells.Length; i++)
        {
            if (!_columnInfos[i].IsPrimaryKey)
                values[_columnInfos[i].Name] = row.RawValue(i) is DBNull ? null : row.RawValue(i);
        }

        try
        {
            await _provider.InsertRowAsync(_tableName, values);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteRowAsync(GridRow row)
    {
        if (ShowConfirmDialog is not null)
        {
            var confirmed = await ShowConfirmDialog($"Delete this row from '{_tableName}'?");
            if (!confirmed) return;
        }

        try
        {
            var pkValues = GetPkValues(row);
            await _provider.DeleteRowAsync(_tableName, pkValues);
            Rows.Remove(row);
            TotalRows--;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SetNullAsync(GridRow row)
    {
        if (_columnInfos.Count == 0) return;

        var vm = BuildEditViewModel("Edit", row);
        // Pre-set all non-PK fields to empty (will be saved as NULL)
        foreach (var f in vm.Fields.Where(f => !f.IsPrimaryKey))
            f.Value = string.Empty;

        if (ShowEditDialog is not null)
            await ShowEditDialog(vm);

        if (!vm.Confirmed) return;

        try
        {
            var pkValues = GetPkValues(row);
            await _provider.UpdateRowAsync(_tableName, ConvertEditFields(vm.Fields), pkValues);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task EditCellAsync(CellEditRequest request)
    {
        if (_columnInfos.Count <= request.ColumnIndex) return;

        var column = _columnInfos[request.ColumnIndex];
        var vm = new CellEditViewModel(_tableName, column.Name, column.DataType, request.Row, request.ColumnIndex)
        {
            ShowJsonTree = true
        };

        if (ShowCellEditDialog is not null)
            await ShowCellEditDialog(vm);

        if (!vm.Confirmed) return;

        try
        {
            var pkValues = GetPkValues(request.Row);
            var parsedValue = ParseValue(column.DataType, vm.Value);
            var updateValues = new Dictionary<string, object?> { [column.Name] = parsedValue };

            await _provider.UpdateRowAsync(_tableName, updateValues, pkValues);
            await LoadPageAsync(CurrentPage);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error($"EditCell failed on '{column.Name}'", ex);
        }
    }

    [RelayCommand]
    private void EnableInlineEdit(CellEditRequest request)
    {
        if (_columnInfos.Count <= request.ColumnIndex)
            return;

        IsInlineEditMode = true;
        SelectedRow = request.Row;
        var columnName = _columnInfos[request.ColumnIndex].Name;
        InlineEditStatus = $"Inline editing enabled on '{columnName}'. Edit directly in the grid and press Enter to save.";
    }

    [RelayCommand]
    private void DisableInlineEdit()
        => IsInlineEditMode = false;

    [RelayCommand]
    private async Task SavePendingChangesAsync()
    {
        if (_pendingCellEdits.Count == 0)
        {
            InlineEditStatus = "No pending changes.";
            return;
        }

        var groupedByRow = _pendingCellEdits
            .GroupBy(k => k.Key.Row)
            .ToList();

        int savedRows = 0;
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            foreach (var rowGroup in groupedByRow)
            {
                var row = rowGroup.Key;
                var pkValues = GetPkValues(row);
                var updateValues = new Dictionary<string, object?>();

                foreach (var change in rowGroup)
                {
                    var idx = change.Key.ColumnIndex;
                    if (idx < 0 || idx >= _columnInfos.Count)
                        continue;

                    var col = _columnInfos[idx];
                    updateValues[col.Name] = ParseValue(col.DataType, change.Value ?? string.Empty);
                }

                if (updateValues.Count == 0)
                    continue;

                await _provider.UpdateRowAsync(_tableName, updateValues, pkValues);
                savedRows++;
            }

            _pendingCellEdits.Clear();
            PendingChangeCount = 0;
            InlineEditStatus = $"Saved changes for {savedRows} row(s).";
            AppLog.Info($"Saved inline pending changes: {savedRows} row(s)");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            InlineEditStatus = "Failed to save pending changes.";
            AppLog.Error("SavePendingChanges failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshTableAsync()
    {
        if (HasPendingChanges && ShowConfirmDialog is not null)
        {
            var discard = await ShowConfirmDialog("You have unsaved cell edits. Discard and refresh?");
            if (!discard) return;
        }

        _pendingCellEdits.Clear();
        PendingChangeCount = 0;
        await LoadPageAsync(CurrentPage);
        InlineEditStatus = "Table refreshed.";
    }

    [RelayCommand]
    private async Task ExportDataAsync(CancellationToken cancellationToken = default)
    {
        if (PickExportPath is null)
        {
            ErrorMessage = "Export file picker is not available.";
            return;
        }

        var format = NormalizeExportFormat(SelectedExportFormat);
        var path = await PickExportPath(format);
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var dataset = await GetExportDatasetAsync(cancellationToken);

            switch (format)
            {
                case "csv":
                    await File.WriteAllTextAsync(path, BuildCsv(dataset.Columns, dataset.Rows), Encoding.UTF8, cancellationToken);
                    break;
                case "json":
                    await File.WriteAllTextAsync(path, BuildJson(dataset.Columns, dataset.Rows), Encoding.UTF8, cancellationToken);
                    break;
                case "sql":
                    await File.WriteAllTextAsync(path, BuildInsertSql(_tableName, dataset.Columns, dataset.Rows), Encoding.UTF8, cancellationToken);
                    break;
                case "xlsx":
                    await Task.Run(() => BuildXlsx(path, dataset.Columns, dataset.Rows), cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported export format: {format}");
            }

            InlineEditStatus = $"Export complete: {dataset.Rows.Count} row(s) to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            InlineEditStatus = "Export failed.";
            AppLog.Error("ExportData failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportSqlAsync(CancellationToken cancellationToken = default)
    {
        if (PickImportPath is null)
        {
            ErrorMessage = "Import file picker is not available.";
            return;
        }

        var path = await PickImportPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var script = await File.ReadAllTextAsync(path, cancellationToken);
            var statements = SplitSqlScript(script);
            int executed = 0;

            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _provider.ExecuteAsync(statement, cancellationToken);
                executed++;
            }

            await LoadPageAsync(CurrentPage, cancellationToken);
            InlineEditStatus = $"Import complete: executed {executed} statement(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            InlineEditStatus = "Import failed.";
            AppLog.Error("ImportSql failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ExecuteDesignSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var statements = SplitSqlScript(sql);
            foreach (var statement in statements)
                await _provider.ExecuteAsync(statement, cancellationToken);

            await LoadAsync(cancellationToken);
            InlineEditStatus = "Design SQL executed successfully.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            InlineEditStatus = "Design SQL execution failed.";
            AppLog.Error("ExecuteDesignSql failed", ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ClipboardRequested is raised by copy commands; the View subscribes and performs the actual write.
    public event Action<string>? ClipboardRequested;

    [RelayCommand]
    private void CopyRow(GridRow row)
        => ClipboardRequested?.Invoke(string.Join("\t", row.Cells.Select(c => c.FullText)));

    [RelayCommand]
    private void CopyCell(CellInfo cell)
        => ClipboardRequested?.Invoke(cell.FullText);

    [RelayCommand]
    private void FilterByCell(CellInfo cell)
    {
        if (_columnInfos.Count <= cell.Index) return;
        var col = _columnInfos[cell.Index].Name;
        FilterText = $"{col} = '{cell.FullText.Replace("'", "''")}'";
        _ = ApplyFilterAsync();
    }

    [RelayCommand]
    private async Task ExpandCellAsync(CellInfo cell)
    {
        if (ShowExpandDialog is null || _columnInfos.Count <= cell.Index) return;
        var colName = _columnInfos[cell.Index].Name;
        var vm = new CellExpandViewModel(colName, cell.FullText);
        await ShowExpandDialog(vm);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private EditRowViewModel BuildEditViewModel(string mode, GridRow? existingRow)
    {
        var names = _columnInfos.Select(c => c.Name).ToList();
        var types = _columnInfos.Select(c => c.DataType).ToList();
        var pks = _columnInfos.Select(c => c.IsPrimaryKey).ToList();
        return new EditRowViewModel(_tableName, mode, names, types, pks, existingRow);
    }

    private Dictionary<string, object?> GetPkValues(GridRow row)
    {
        var pk = new Dictionary<string, object?>();
        for (int i = 0; i < _columnInfos.Count && i < row.Cells.Length; i++)
        {
            if (_columnInfos[i].IsPrimaryKey)
                pk[_columnInfos[i].Name] = row.RawValue(i) is DBNull ? null : row.RawValue(i);
        }
        // Fallback: use first column if no explicit PK found
        if (pk.Count == 0 && _columnInfos.Count > 0)
            pk[_columnInfos[0].Name] = row.RawValue(0) is DBNull ? null : row.RawValue(0);
        return pk;
    }

    private Dictionary<string, object?> ConvertEditFields(IEnumerable<FieldEdit> fields)
    {
        var values = new Dictionary<string, object?>();
        foreach (var field in fields)
            values[field.ColumnName] = ParseValue(field.DataType, field.Value);
        return values;
    }

    private static object? ParseValue(string dataType, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalizedType = dataType.ToLowerInvariant();

        if (normalizedType.Contains("bool"))
        {
            if (bool.TryParse(input, out var b)) return b;
            if (input == "1") return true;
            if (input == "0") return false;
            return input;
        }

        if (normalizedType.Contains("int") || normalizedType is "serial" or "bigserial")
        {
            if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;
            return input;
        }

        if (normalizedType.Contains("numeric") || normalizedType.Contains("decimal") || normalizedType.Contains("real") || normalizedType.Contains("double"))
        {
            if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                return dec;
            return input;
        }

        if (normalizedType.Contains("timestamp") || normalizedType == "date" || normalizedType.Contains("time"))
        {
            var styles = DateTimeStyles.AssumeLocal;
            var formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-dd",
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy",
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy"
            };

            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, styles, out var dtExact))
                return dtExact;

            if (DateTime.TryParse(input, CultureInfo.CurrentCulture, styles, out var dtLocal))
                return dtLocal;

            return input;
        }

        if (normalizedType.Contains("uuid") && Guid.TryParse(input, out var guid))
            return guid;

        return input;
    }

    private void OnRowCellEdited(GridRow row, int columnIndex, string? newText)
    {
        if (!IsInlineEditMode)
            return;

        if (columnIndex < 0 || columnIndex >= _columnInfos.Count)
            return;

        _pendingCellEdits[(row, columnIndex)] = newText;
        PendingChangeCount = _pendingCellEdits.Count;

        var column = _columnInfos[columnIndex];
        InlineEditStatus = $"Edited '{column.Name}'. {PendingChangeCount} pending change(s). Click Save to persist.";
    }

    private async Task<(List<string> Columns, List<object?[]> Rows)> GetExportDatasetAsync(CancellationToken cancellationToken)
    {
        var columns = ColumnNames.ToList();
        var rows = new List<object?[]>();

        if (!ExportFullTable)
        {
            rows.AddRange(Rows.Select(r => r.AllValues.ToArray()));
            return (columns, rows);
        }

        const int exportPageSize = 1000;
        int page = 1;

        while (true)
        {
            var result = await _provider.GetRowsAsync(
                _tableName,
                page,
                exportPageSize,
                string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
                ct: cancellationToken);

            rows.AddRange(result.Data.Select(r => r.ToArray()));

            if (!result.HasNextPage)
                break;

            page++;
        }

        return (columns, rows);
    }

    private static string NormalizeExportFormat(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "csv" or "json" or "sql" or "xlsx" ? normalized : "csv";
    }

    internal static string BuildCsv(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(";", columns.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            var values = row.Select(v => EscapeCsv(ToInvariantText(v)));
            builder.AppendLine(string.Join(";", values));
        }

        return builder.ToString();
    }

    internal static string BuildJson(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        var payload = new List<Dictionary<string, object?>>();

        foreach (var row in rows)
        {
            var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count && i < row.Length; i++)
                item[columns[i]] = row[i] is DBNull ? null : row[i];
            payload.Add(item);
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string BuildInsertSql(string tableName, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        if (rows.Count == 0)
            return $"-- No rows to export for {tableName}";

        var builder = new StringBuilder();
        var table = QuoteSqlIdentifier(tableName);
        var cols = string.Join(", ", columns.Select(QuoteSqlIdentifier));

        foreach (var row in rows)
        {
            var values = string.Join(", ", row.Select(ToSqlLiteral));
            builder.Append("INSERT INTO ")
                .Append(table)
                .Append(" (")
                .Append(cols)
                .Append(") VALUES (")
                .Append(values)
                .AppendLine(");");
        }

        return builder.ToString();
    }

    private const int XlsxMaxCellLength = 32000;

    internal static void BuildXlsx(string path, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Export");

        for (int c = 0; c < columns.Count; c++)
            sheet.Cell(1, c + 1).Value = columns[c];

        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < columns.Count && c < row.Length; c++)
            {
                var text = row[c] is DBNull ? string.Empty : ToInvariantText(row[c]);
                if (text.Length > XlsxMaxCellLength)
                    text = text[..XlsxMaxCellLength];
                sheet.Cell(r + 2, c + 1).Value = text;
            }
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    internal static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(';') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    internal static string ToInvariantText(object? value)
    {
        if (value is null || value is DBNull)
            return string.Empty;

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ToSqlLiteral(object? value)
    {
        if (value is null || value is DBNull)
            return "NULL";

        return value switch
        {
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "TRUE" : "FALSE",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss zzz}'",
            Guid g => $"'{g}'",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "NULL",
            _ => $"'{(value.ToString() ?? string.Empty).Replace("'", "''")}'"
        };
    }

    private static string QuoteSqlIdentifier(string name)
        => string.Join('.', name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => $"\"{part.Replace("\"", "\"\"")}\""));

    private static List<string> SplitSqlScript(string script)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < script.Length; i++)
        {
            var ch = script[i];

            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < script.Length && script[i + 1] == '\'')
                {
                    current.Append(ch);
                    current.Append(script[++i]);
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && i + 1 < script.Length && script[i + 1] == '"')
                {
                    current.Append(ch);
                    current.Append(script[++i]);
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
            }

            if (ch == ';' && !inSingleQuote && !inDoubleQuote)
            {
                var statement = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                    result.Add(statement);
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            result.Add(tail);

        return result;
    }

}

public sealed record CellEditRequest(GridRow Row, int ColumnIndex);
