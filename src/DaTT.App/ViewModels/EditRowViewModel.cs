using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

public sealed class FieldEdit : ObservableObject
{
    public string ColumnName { get; }
    public string DataType { get; }
    public bool IsPrimaryKey { get; }

    public bool IsDateTimeField { get; }
    public bool IsDateOnlyField { get; }
    public bool IsTimeOnlyField { get; }
    public bool IsTextInput => !IsDateTimeField && !IsDateOnlyField && !IsTimeOnlyField;

    private string _value;
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    private DateTime? _datePart;
    public DateTime? DatePart
    {
        get => _datePart;
        set { if (SetProperty(ref _datePart, value)) SyncValueFromPickers(); }
    }

    private TimeSpan? _timePart;
    public TimeSpan? TimePart
    {
        get => _timePart;
        set { if (SetProperty(ref _timePart, value)) SyncValueFromPickers(); }
    }

    private void SyncValueFromPickers()
    {
        if (IsDateTimeField)
        {
            if (_datePart.HasValue)
            {
                var combined = _datePart.Value.Date.Add(_timePart ?? TimeSpan.Zero);
                _value = combined.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else
                _value = string.Empty;
            OnPropertyChanged(nameof(Value));
        }
        else if (IsDateOnlyField)
        {
            _value = _datePart.HasValue
                ? _datePart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : string.Empty;
            OnPropertyChanged(nameof(Value));
        }
        else if (IsTimeOnlyField)
        {
            _value = _timePart.HasValue
                ? _timePart.Value.ToString(@"hh\:mm\:ss")
                : string.Empty;
            OnPropertyChanged(nameof(Value));
        }
    }

    private static (bool isDateTime, bool isDateOnly, bool isTimeOnly) DetectKind(string dataType)
    {
        var bare = dataType.ToLowerInvariant().Split('(')[0].Trim();
        bool isDateTime = bare == "datetime" || bare.StartsWith("timestamp");
        bool isDate = bare == "date";
        bool isTime = (bare == "time" || bare == "timetz") && !isDateTime;
        return (isDateTime, isDate, isTime);
    }

    public FieldEdit(string columnName, string dataType, bool isPrimaryKey, string currentValue)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsPrimaryKey = isPrimaryKey;
        _value = currentValue;

        (IsDateTimeField, IsDateOnlyField, IsTimeOnlyField) = DetectKind(dataType);

        if (IsDateTimeField)
        {
            if (DateTime.TryParse(currentValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                _datePart = dt;
                _timePart = dt.TimeOfDay;
            }
        }
        else if (IsDateOnlyField)
        {
            if (DateTime.TryParse(currentValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                _datePart = dt;
        }
        else if (IsTimeOnlyField)
        {
            if (TimeSpan.TryParse(currentValue, out var ts))
                _timePart = ts;
        }
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
