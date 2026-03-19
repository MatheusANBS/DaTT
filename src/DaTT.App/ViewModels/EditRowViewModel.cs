using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

public sealed class FieldEdit : ObservableObject
{
    public string ColumnName { get; }
    public string DataType { get; }
    public bool IsPrimaryKey { get; }

    private string _value;
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public FieldEdit(string columnName, string dataType, bool isPrimaryKey, string currentValue)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsPrimaryKey = isPrimaryKey;
        _value = currentValue;
    }
}

public sealed class EditRowViewModel : ViewModelBase
{
    public string TableName { get; }
    public string Mode { get; }  // "Edit" or "Insert"
    public ObservableCollection<FieldEdit> Fields { get; } = [];

    public bool Confirmed { get; private set; }

    public EditRowViewModel(string tableName, string mode, IReadOnlyList<string> columnNames,
        IReadOnlyList<string> dataTypes, IReadOnlyList<bool> pkFlags, GridRow? existingRow)
    {
        TableName = tableName;
        Mode = mode;

        for (int i = 0; i < columnNames.Count; i++)
        {
            var currentValue = existingRow is null
                ? string.Empty
                : existingRow.Cells.Length > i
                    ? (existingRow.Cells[i].IsNull ? string.Empty : existingRow.Cells[i].FullText)
                    : string.Empty;

            Fields.Add(new FieldEdit(
                columnNames[i],
                dataTypes.Count > i ? dataTypes[i] : "text",
                pkFlags.Count > i && pkFlags[i],
                currentValue
            ));
        }
    }

    public Dictionary<string, object?> ToValueDictionary()
        => Fields.ToDictionary(f => f.ColumnName, f => (object?)(f.Value == string.Empty ? null : f.Value));

    public void Confirm() => Confirmed = true;
}
