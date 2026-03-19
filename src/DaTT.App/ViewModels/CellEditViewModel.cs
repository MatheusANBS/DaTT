using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

public sealed partial class CellEditViewModel : ViewModelBase
{
    public string TableName { get; }
    public string ColumnName { get; }
    public string DataType { get; }
    public GridRow Row { get; }
    public int ColumnIndex { get; }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isJson;

    [ObservableProperty]
    private bool _showJsonTree;

    public bool Confirmed { get; private set; }

    public ObservableCollection<JsonTreeNode> JsonNodes { get; } = [];

    public CellEditViewModel(string tableName, string columnName, string dataType, GridRow row, int columnIndex)
    {
        TableName = tableName;
        ColumnName = columnName;
        DataType = dataType;
        Row = row;
        ColumnIndex = columnIndex;

        _value = row.Cells.Length > columnIndex
            ? (row.Cells[columnIndex].IsNull ? string.Empty : row.Cells[columnIndex].FullText)
            : string.Empty;
        RebuildJsonTree();
    }

    partial void OnValueChanged(string value)
    {
        RebuildJsonTree();
    }

    public void Confirm() => Confirmed = true;

    private void RebuildJsonTree()
    {
        JsonNodes.Clear();

        var trimmed = Value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
        {
            IsJson = false;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            IsJson = true;
            JsonNodes.Add(JsonTreeNode.FromElement("$", doc.RootElement));
        }
        catch
        {
            IsJson = false;
        }
    }
}

public sealed class JsonTreeNode
{
    public string Key { get; }
    public string? Value { get; }
    public ObservableCollection<JsonTreeNode> Children { get; } = [];

    public JsonTreeNode(string key, string? value)
    {
        Key = key;
        Value = value;
    }

    public static JsonTreeNode FromElement(string key, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var node = new JsonTreeNode(key, "{ }");
            foreach (var prop in element.EnumerateObject())
                node.Children.Add(FromElement(prop.Name, prop.Value));
            return node;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var node = new JsonTreeNode(key, "[ ]");
            int i = 0;
            foreach (var item in element.EnumerateArray())
            {
                node.Children.Add(FromElement($"[{i}]", item));
                i++;
            }
            return node;
        }

        return new JsonTreeNode(key, element.ToString());
    }
}
