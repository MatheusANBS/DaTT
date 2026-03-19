using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;

namespace DaTT.Providers;

public sealed class ElasticSearchProvider : IDatabaseProvider
{
    private readonly ILogger<ElasticSearchProvider> _logger;
    private HttpClient? _client;
    private Uri? _baseUri;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ElasticSearchProvider(ILogger<ElasticSearchProvider> logger)
    {
        _logger = logger;
    }

    public string EngineName => "ElasticSearch";
    public string[] SupportedSchemes => ["elasticsearch", "es"];
    public ISqlDialect Dialect => NoSqlDialect.Instance;
    public bool IsConnected => _client is not null && _baseUri is not null;

    public async Task ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        await DisposeAsync();

        var parsed = ParseConnection(connectionString);
        _baseUri = parsed.BaseUri;

        _client = new HttpClient
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(20)
        };

        if (parsed.AuthHeader is not null)
            _client.DefaultRequestHeaders.Authorization = parsed.AuthHeader;

        var ok = await TestPingAsync(ct);
        if (!ok)
            throw new InvalidOperationException("ElasticSearch ping failed.");

        _logger.LogInformation("ElasticSearch connection opened at {BaseUri}", _baseUri);
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            var parsed = ParseConnection(connectionString);
            using var client = new HttpClient { BaseAddress = parsed.BaseUri, Timeout = TimeSpan.FromSeconds(10) };

            if (parsed.AuthHeader is not null)
                client.DefaultRequestHeaders.Authorization = parsed.AuthHeader;

            using var response = await client.GetAsync("/", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ElasticSearch connection test failed");
            return false;
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        using var response = await _client!.GetAsync("/", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var response = await SendAsync(HttpMethod.Get, "/_cat/indices?format=json&h=index,docs.count", null, ct);

        var array = JsonNode.Parse(response)?.AsArray();
        if (array is null)
            return [];

        return array
            .Select(node => new TableInfo(
                Name: node?["index"]?.GetValue<string>() ?? "",
                RowCount: long.TryParse(node?["docs.count"]?.GetValue<string>(), out var cnt) ? cnt : null
            ))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Distinct()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var response = await SendAsync(HttpMethod.Get, $"/{EscapeSegment(table)}/_mapping", null, ct);
        var root = JsonNode.Parse(response) as JsonObject;

        var properties = root?[table]?["mappings"]?["properties"] as JsonObject;
        var columns = new List<ColumnMeta>
        {
            new("_id", "keyword", SimpleType: "keyword", Key: "PRI", IsNullable: false, OrdinalPosition: 0)
        };

        if (properties is not null)
        {
            int ordinal = 1;
            foreach (var kv in properties)
            {
                var dataType = kv.Value?["type"]?.GetValue<string>() ?? "object";
                columns.Add(new ColumnMeta(kv.Key, dataType, SimpleType: dataType, IsNullable: true, OrdinalPosition: ordinal));
                ordinal++;
            }
        }

        return columns;
    }

    public Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IndexMeta>>([]);

    public Task<IReadOnlyList<ForeignKeyMeta>> GetForeignKeysAsync(string table, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ForeignKeyMeta>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetTriggersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetProceduresAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetFunctionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<IReadOnlyList<DatabaseObjectInfo>> GetUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DatabaseObjectInfo>>([]);

    public Task<string?> GetTableSourceAsync(string table, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetViewSourceAsync(string view, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetProcedureSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetFunctionSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> GetTriggerSourceAsync(string name, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public async Task<ExecuteResult> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var command = ParseCommand(sql);
            var response = await SendAsync(command.Method, command.Path, command.Body, ct);
            sw.Stop();

            var parsed = JsonNode.Parse(response);
            var col = new ColumnMeta("json", "text", OrdinalPosition: 0);

            if (parsed is JsonArray array)
            {
                var rows = array.Select(item => new object?[] { item?.ToJsonString(JsonOptions) ?? string.Empty }).ToList();
                return ExecuteResult.FromRows([col], rows, sw.Elapsed);
            }

            return ExecuteResult.FromRows([col], [[parsed?.ToJsonString(JsonOptions) ?? string.Empty]], sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecuteResult.FromError(ex.Message);
        }
    }

    public async Task<PagedResult<IReadOnlyList<object?[]>>> GetRowsAsync(
        string table, int page, int pageSize,
        string? filter = null, string? orderBy = null,
        CancellationToken ct = default)
    {
        EnsureConnected();

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        var from = (safePage - 1) * safePageSize;

        var columns = await GetColumnsAsync(table, ct);
        var colNames = columns.OrderBy(c => c.OrdinalPosition).Select(c => c.Name).ToList();

        JsonObject body = BuildSearchBody(from, safePageSize, filter, orderBy);

        var response = await SendAsync(HttpMethod.Post, $"/{EscapeSegment(table)}/_search", body.ToJsonString(JsonOptions), ct);
        var json = JsonNode.Parse(response) as JsonObject;

        var totalToken = json?["hits"]?["total"]?["value"];
        var totalRows = totalToken?.GetValue<int>() ?? 0;

        var hits = json?["hits"]?["hits"] as JsonArray;
        var rows = new List<object?[]>();

        if (hits is not null)
        {
            foreach (var hitNode in hits)
            {
                var hit = hitNode as JsonObject;
                var source = hit?["_source"] as JsonObject;
                var row = new object?[colNames.Count];

                for (int i = 0; i < colNames.Count; i++)
                {
                    var col = colNames[i];
                    if (col == "_id")
                    {
                        row[i] = hit?["_id"]?.GetValue<string>();
                        continue;
                    }

                    var valueNode = source?[col];
                    row[i] = ToClrValue(valueNode);
                }

                rows.Add(row);
            }
        }

        return new PagedResult<IReadOnlyList<object?[]>>(rows, totalRows, safePage, safePageSize);
    }

    public async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var payload = JsonSerializer.Serialize(values, JsonOptions);
        await SendAsync(HttpMethod.Post, $"/{EscapeSegment(table)}/_doc", payload, ct);
    }

    public async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();

        if (!pkValues.TryGetValue("_id", out var id) || id is null)
            throw new InvalidOperationException("ElasticSearch update requires _id primary key value.");

        var payload = JsonSerializer.Serialize(new { doc = newValues }, JsonOptions);
        await SendAsync(HttpMethod.Post, $"/{EscapeSegment(table)}/_update/{EscapeSegment(id.ToString()!)}", payload, ct);
    }

    public async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();

        if (!pkValues.TryGetValue("_id", out var id) || id is null)
            throw new InvalidOperationException("ElasticSearch delete requires _id primary key value.");

        await SendAsync(HttpMethod.Delete, $"/{EscapeSegment(table)}/_doc/{EscapeSegment(id.ToString()!)}", null, ct);
    }

    public Task CreateTableAsync(string ddl, CancellationToken ct = default)
    {
        var indexName = ddl.Trim();
        if (string.IsNullOrWhiteSpace(indexName))
            throw new InvalidOperationException("ElasticSearch create index expects index name as command payload.");

        return SendAsync(HttpMethod.Put, $"/{EscapeSegment(indexName)}", "{}", ct);
    }

    public Task DropTableAsync(string table, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/{EscapeSegment(table)}", null, ct);

    public Task TruncateTableAsync(string table, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/{EscapeSegment(table)}/_delete_by_query", "{\"query\":{\"match_all\":{}}}", ct);

    public Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => throw new NotSupportedException("ElasticSearch does not support direct index rename. Use reindex + alias strategy.");

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        _baseUri = null;
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (_client is null || _baseUri is null)
            throw new InvalidOperationException("ElasticSearch connection is not open.");
    }

    private async Task<bool> TestPingAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _client!.GetAsync("/", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        EnsureConnected();
        using var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _client!.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElasticSearch request failed ({(int)response.StatusCode}): {content}");

        return string.IsNullOrWhiteSpace(content) ? "{}" : content;
    }

    private static (HttpMethod Method, string Path, string? Body) ParseCommand(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Command cannot be empty.");

        var firstNewLine = trimmed.IndexOf('\n');
        var header = firstNewLine >= 0 ? trimmed[..firstNewLine].Trim() : trimmed;
        var body = firstNewLine >= 0 ? trimmed[(firstNewLine + 1)..].Trim() : null;

        var pieces = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 1)
        {
            return (HttpMethod.Get, EnsureLeadingSlash(pieces[0]), body);
        }

        var method = new HttpMethod(pieces[0].ToUpperInvariant());
        var path = EnsureLeadingSlash(pieces[1]);
        return (method, path, string.IsNullOrWhiteSpace(body) ? null : body);
    }

    private static JsonObject BuildSearchBody(int from, int size, string? filter, string? orderBy)
    {
        var body = new JsonObject
        {
            ["from"] = from,
            ["size"] = size
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            body["query"] = new JsonObject
            {
                ["query_string"] = new JsonObject
                {
                    ["query"] = filter
                }
            };
        }
        else
        {
            body["query"] = new JsonObject { ["match_all"] = new JsonObject() };
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            var orderParts = orderBy.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var field = orderParts[0];
            var direction = orderParts.Length > 1 ? orderParts[1].ToLowerInvariant() : "asc";
            body["sort"] = new JsonArray
            {
                new JsonObject
                {
                    [field] = new JsonObject { ["order"] = direction }
                }
            };
        }

        return body;
    }

    private static object? ToClrValue(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue valueNode)
        {
            if (valueNode.TryGetValue(out string? str)) return str;
            if (valueNode.TryGetValue(out long l)) return l;
            if (valueNode.TryGetValue(out double d)) return d;
            if (valueNode.TryGetValue(out bool b)) return b;
            return valueNode.ToJsonString(JsonOptions);
        }

        return node.ToJsonString(JsonOptions);
    }

    private static string EnsureLeadingSlash(string path)
        => path.StartsWith('/') ? path : "/" + path;

    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(value);

    private static (Uri BaseUri, AuthenticationHeaderValue? AuthHeader) ParseConnection(string connectionString)
    {
        var uri = new Uri(connectionString);
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "http" or "https")
        {
            AuthenticationHeaderValue? directAuth = null;
            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri.UserInfo)));
                directAuth = new AuthenticationHeaderValue("Basic", base64);
            }

            var directQuery = ParseQuery(uri.Query);
            if (directQuery.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token))
                directAuth = new AuthenticationHeaderValue("ApiKey", token);

            var directBuilder = new UriBuilder(uri)
            {
                Path = "/"
            };

            return (directBuilder.Uri, directAuth);
        }

        if (scheme is not "elasticsearch" and not "es")
            throw new NotSupportedException("ElasticSearch provider expects connection string starting with elasticsearch://, es://, http:// or https://");

        var builder = new UriBuilder
        {
            Scheme = "http",
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 9200,
            Path = "/"
        };

        AuthenticationHeaderValue? auth = null;
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(uri.UserInfo)));
            auth = new AuthenticationHeaderValue("Basic", base64);
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("token", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
            auth = new AuthenticationHeaderValue("ApiKey", apiKey);

        return (builder.Uri, auth);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}
