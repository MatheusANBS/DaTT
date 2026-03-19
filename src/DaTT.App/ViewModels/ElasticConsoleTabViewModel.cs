using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;

namespace DaTT.App.ViewModels;

public partial class ElasticConsoleTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;

    public override string Title => "Elastic Console";

    [ObservableProperty]
    private string _httpMethod = "GET";

    [ObservableProperty]
    private string _requestPath = "/_cat/indices?format=json";

    [ObservableProperty]
    private string _requestBody = string.Empty;

    [ObservableProperty]
    private string _responseText = string.Empty;

    [ObservableProperty]
    private string? _selectedIndex;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Indices { get; } = [];
    public IReadOnlyList<string> HttpMethods { get; } = ["GET", "POST", "PUT", "DELETE"];

    public ElasticConsoleTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshIndicesAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshIndicesAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var indexes = await _provider.GetTablesAsync(cancellationToken);
            Indices.Clear();
            foreach (var index in indexes)
                Indices.Add(index.Name);

            if (SelectedIndex is null && Indices.Count > 0)
                SelectedIndex = Indices[0];

            StatusMessage = $"Loaded {Indices.Count} index(es).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Failed to load indices.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void QuerySelectedIndex()
    {
        if (string.IsNullOrWhiteSpace(SelectedIndex))
            return;

        HttpMethod = "POST";
        RequestPath = $"/{SelectedIndex}/_search";
        RequestBody = "{\n  \"size\": 50,\n  \"query\": { \"match_all\": {} }\n}";
    }

    [RelayCommand]
    private void CountSelectedIndex()
    {
        if (string.IsNullOrWhiteSpace(SelectedIndex))
            return;

        HttpMethod = "GET";
        RequestPath = $"/{SelectedIndex}/_count";
        RequestBody = string.Empty;
    }

    [RelayCommand]
    private void FormatRequestBody()
    {
        if (string.IsNullOrWhiteSpace(RequestBody))
            return;

        try
        {
            using var doc = JsonDocument.Parse(RequestBody);
            RequestBody = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            ErrorMessage = "Request body is not valid JSON.";
        }
    }

    [RelayCommand]
    private async Task ExecuteRequestAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(RequestPath))
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var method = string.IsNullOrWhiteSpace(HttpMethod) ? "GET" : HttpMethod.Trim().ToUpperInvariant();
            var command = BuildCommand(method, RequestPath, RequestBody);

            var result = await _provider.ExecuteAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error;
                return;
            }

            ResponseText = string.Join(Environment.NewLine, result.Rows.Select(r => r.FirstOrDefault()?.ToString() ?? string.Empty));
            StatusMessage = $"{method} {RequestPath} finished in {result.ExecutionTime.TotalMilliseconds:F0} ms.";

            if (RequestPath.Contains("_cat/indices", StringComparison.OrdinalIgnoreCase))
                await RefreshIndicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildCommand(string method, string path, string body)
    {
        var safePath = path.StartsWith('/') ? path : "/" + path;
        if (string.IsNullOrWhiteSpace(body))
            return $"{method} {safePath}";

        return $"{method} {safePath}{Environment.NewLine}{body}";
    }
}
