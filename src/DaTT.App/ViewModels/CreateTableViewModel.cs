using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DaTT.App.ViewModels;

public sealed partial class CreateTableViewModel : ViewModelBase
{
    [ObservableProperty] private string _tableName = string.Empty;
    [ObservableProperty] private string _sqlPreview = string.Empty;
    [ObservableProperty] private string _statusMessage = "Define columns and generate the CREATE TABLE script.";
    [ObservableProperty] private bool _isStatusError;

    public ObservableCollection<ColumnDefinition> Columns { get; } = [];

    public bool Confirmed { get; private set; }
    public string GeneratedSql => SqlPreview;

    public CreateTableViewModel()
    {
        Columns.Add(new ColumnDefinition { Name = "id", DataType = "SERIAL", IsPrimaryKey = true, IsNullable = false });
    }

    [RelayCommand]
    private void AddColumn()
    {
        Columns.Add(new ColumnDefinition());
        RegenerateSql();
    }

    [RelayCommand]
    private void RemoveColumn(ColumnDefinition column)
    {
        Columns.Remove(column);
        RegenerateSql();
    }

    [RelayCommand]
    private void MoveColumnUp(ColumnDefinition column)
    {
        var index = Columns.IndexOf(column);
        if (index > 0)
        {
            Columns.Move(index, index - 1);
            RegenerateSql();
        }
    }

    [RelayCommand]
    private void MoveColumnDown(ColumnDefinition column)
    {
        var index = Columns.IndexOf(column);
        if (index >= 0 && index < Columns.Count - 1)
        {
            Columns.Move(index, index + 1);
            RegenerateSql();
        }
    }

    [RelayCommand]
    private void GenerateSql()
    {
        RegenerateSql();
    }

    public void Confirm()
    {
        if (!Validate())
            return;

        RegenerateSql();
        Confirmed = true;
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(TableName) || !IsSafeIdentifier(TableName))
        {
            StatusMessage = "Invalid table name. Use only letters, digits, and underscores.";
            IsStatusError = true;
            return false;
        }

        if (Columns.Count == 0)
        {
            StatusMessage = "Add at least one column.";
            IsStatusError = true;
            return false;
        }

        foreach (var col in Columns)
        {
            if (string.IsNullOrWhiteSpace(col.Name) || !IsSafeIdentifier(col.Name))
            {
                StatusMessage = $"Invalid column name: '{col.Name}'.";
                IsStatusError = true;
                return false;
            }

            if (string.IsNullOrWhiteSpace(col.DataType))
            {
                StatusMessage = $"Column '{col.Name}' has no data type.";
                IsStatusError = true;
                return false;
            }
        }

        var duplicates = Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            StatusMessage = $"Duplicate column name: '{duplicates[0]}'.";
            IsStatusError = true;
            return false;
        }

        IsStatusError = false;
        return true;
    }

    private void RegenerateSql()
    {
        if (!Validate())
        {
            SqlPreview = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(QuoteIdentifier(TableName)).AppendLine(" (");

        var definitions = new List<string>();

        foreach (var col in Columns)
        {
            var line = $"    {QuoteIdentifier(col.Name)} {col.DataType}";
            if (!col.IsNullable)
                line += " NOT NULL";
            if (!string.IsNullOrWhiteSpace(col.DefaultValue))
                line += $" DEFAULT {col.DefaultValue}";
            definitions.Add(line);
        }

        var pkColumns = Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => QuoteIdentifier(c.Name))
            .ToList();

        if (pkColumns.Count > 0)
            definitions.Add($"    PRIMARY KEY ({string.Join(", ", pkColumns)})");

        sb.AppendLine(string.Join($",{Environment.NewLine}", definitions));
        sb.Append(");");

        SqlPreview = sb.ToString();
        StatusMessage = "SQL generated successfully.";
        IsStatusError = false;
    }

    private static bool IsSafeIdentifier(string identifier)
        => Regex.IsMatch(identifier, @"^[A-Za-z_][A-Za-z0-9_.]*$");

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";
}

public sealed partial class ColumnDefinition : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dataType = "VARCHAR(255)";
    [ObservableProperty] private bool _isNullable = true;
    [ObservableProperty] private bool _isPrimaryKey;
    [ObservableProperty] private string _defaultValue = string.Empty;
}
