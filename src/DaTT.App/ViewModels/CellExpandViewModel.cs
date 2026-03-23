using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DaTT.App.ViewModels;

public sealed partial class CellExpandViewModel : ViewModelBase
{
    public string ColumnName { get; }
    public string RawText { get; }
    public bool IsJson { get; }

    [ObservableProperty]
    private JsonViewMode _textFormat = JsonViewMode.Vertical;

    [ObservableProperty]
    private string _displayContent = string.Empty;

    public bool IsFlat => _textFormat == JsonViewMode.Flat;
    public bool IsVertical => _textFormat == JsonViewMode.Vertical;

    public CellExpandViewModel(string columnName, string rawText)
    {
        ColumnName = columnName;
        RawText = rawText;

        var trimmed = rawText.TrimStart();
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryFormatJson(rawText, out var pretty))
        {
            IsJson = true;
            _displayContent = pretty!;
        }
        else
        {
            IsJson = false;
            _displayContent = rawText;
        }
    }

    partial void OnTextFormatChanged(JsonViewMode value)
    {
        OnPropertyChanged(nameof(IsFlat));
        OnPropertyChanged(nameof(IsVertical));
        if (!IsJson) return;
        DisplayContent = value == JsonViewMode.Vertical
            ? (TryFormatJson(RawText, out var pretty) ? pretty! : RawText)
            : CompactJson(RawText);
    }

    private static bool TryFormatJson(string input, out string? result)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            result = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    private static string CompactJson(string input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch
        {
            return input;
        }
    }
}
