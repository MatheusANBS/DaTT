using System.Text.Json;

namespace DaTT.App.ViewModels;

public sealed class CellExpandViewModel : ViewModelBase
{
    public string ColumnName { get; }
    public string RawText { get; }
    public string FormattedContent { get; }
    public bool IsJson { get; }

    public CellExpandViewModel(string columnName, string rawText)
    {
        ColumnName = columnName;
        RawText = rawText;

        // Try to detect and pretty-print JSON
        var trimmed = rawText.TrimStart();
        if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryFormatJson(rawText, out var pretty))
        {
            IsJson = true;
            FormattedContent = pretty!;
        }
        else
        {
            IsJson = false;
            FormattedContent = rawText;
        }
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
}
