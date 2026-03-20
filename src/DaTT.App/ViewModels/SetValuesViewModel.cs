using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

// #region Enums
public enum SetValueMode
{
    Null,
    Empty,
    Custom
}
// #endregion Enums

// #region ColumnItem
public sealed partial class SetValueColumnItem : ObservableObject
{
    [ObservableProperty] private bool _isChecked;

    public string ColumnName { get; }
    public string DataType { get; }
    public bool IsEnabled { get; }

    public SetValueColumnItem(string columnName, string dataType, bool isPrimaryKey)
    {
        ColumnName = columnName;
        DataType = dataType;
        IsEnabled = !isPrimaryKey;
        _isChecked = !isPrimaryKey;
    }
}
// #endregion ColumnItem

// #region ViewModel
public sealed partial class SetValuesViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomValueVisible))]
    private SetValueMode _targetValueMode = SetValueMode.Null;

    [ObservableProperty] private string _customValue = string.Empty;

    public ObservableCollection<SetValueColumnItem> ColumnItems { get; } = [];
    public bool Confirmed { get; private set; }
    public bool IsCustomValueVisible => TargetValueMode == SetValueMode.Custom;

    public bool IsNullMode
    {
        get => TargetValueMode == SetValueMode.Null;
        set { if (value) TargetValueMode = SetValueMode.Null; }
    }

    public bool IsEmptyMode
    {
        get => TargetValueMode == SetValueMode.Empty;
        set { if (value) TargetValueMode = SetValueMode.Empty; }
    }

    public bool IsCustomMode
    {
        get => TargetValueMode == SetValueMode.Custom;
        set { if (value) TargetValueMode = SetValueMode.Custom; }
    }

    partial void OnTargetValueModeChanged(SetValueMode value)
    {
        OnPropertyChanged(nameof(IsNullMode));
        OnPropertyChanged(nameof(IsEmptyMode));
        OnPropertyChanged(nameof(IsCustomMode));
    }

    public SetValuesViewModel(IEnumerable<(string Name, string DataType, bool IsPrimaryKey)> columns)
    {
        foreach (var (name, type, isPk) in columns)
            ColumnItems.Add(new SetValueColumnItem(name, type, isPk));
    }

    public void Confirm() => Confirmed = true;

    public object? GetTargetValue() => TargetValueMode switch
    {
        SetValueMode.Null => null,
        SetValueMode.Empty => string.Empty,
        SetValueMode.Custom => CustomValue,
        _ => null
    };
}
// #endregion ViewModel
