using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

/// <summary>
/// Wraps a raw database row (object?[]) with a typed int indexer so that
/// Avalonia DataGridTextColumn bindings using "[N]" paths resolve correctly.
/// Also provides per-cell display strings (truncated) and full raw values.
/// </summary>
public sealed class GridRow : ObservableObject
{
    private readonly object?[] _values;

    public GridRow(object?[] values)
    {
        _values = values;
        Cells = values.Select((v, i) => new CellInfo(i, v)).ToArray();
    }

    public CellInfo[] Cells { get; }

    // Indexed binding for DataGridTextColumn: "[N]"
    public string? this[int index]
    {
        get => index < Cells.Length ? Cells[index].Display : null;
        set
        {
            if (index < 0 || index >= _values.Length)
                return;

            var nextText = value ?? string.Empty;
            var currentText = _values[index] is null || _values[index] is DBNull
                ? string.Empty
                : Convert.ToString(_values[index]) ?? string.Empty;

            if (string.Equals(currentText, nextText, StringComparison.Ordinal))
                return;

            _values[index] = nextText;
            Cells[index] = new CellInfo(index, nextText);

            // Refresh indexed bindings in the DataGrid.
            OnPropertyChanged("Item[]");
            CellEdited?.Invoke(this, index, nextText);
        }
    }

    public int Length => _values.Length;
    public object? RawValue(int index) => index < _values.Length ? _values[index] : null;
    public object?[] AllValues => _values;

    public event Action<GridRow, int, string?>? CellEdited;
}

public sealed class CellInfo
{
    public int Index { get; }
    public object? Raw { get; }
    public string Display { get; }
    public bool IsNull { get; }
    public bool IsJson { get; }
    public int FullLength { get; }

    public CellInfo(int index, object? raw)
    {
        Index = index;
        Raw = raw;
        IsNull = raw is null || raw is DBNull;

        var full = IsNull ? string.Empty : Convert.ToString(raw) ?? string.Empty;
        FullLength = full.Length;
        Display = IsNull ? "(NULL)" : full;

        var trimmed = full.TrimStart();
        IsJson = trimmed.Length > 0
                 && (trimmed[0] == '{' || trimmed[0] == '[')
                 && IsValidJson(trimmed);
    }

    public string FullText => IsNull ? "(NULL)" : Convert.ToString(Raw) ?? string.Empty;

    private static bool IsValidJson(string text)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
