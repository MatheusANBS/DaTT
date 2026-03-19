using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DaTT.Providers;

public sealed class RedisProvider : IDatabaseProvider
{
    private readonly ILogger<RedisProvider> _logger;
    private ConnectionMultiplexer? _mux;
    private IDatabase? _db;
    private IServer? _server;
    private int _databaseIndex;

    public RedisProvider(ILogger<RedisProvider> logger)
    {
        _logger = logger;
    }

    public string EngineName => "Redis";
    public string[] SupportedSchemes => ["redis"];
    public ISqlDialect Dialect => NoSqlDialect.Instance;
    public bool IsConnected => _mux is not null && _db is not null && _server is not null;

    public async Task ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        await DisposeAsync();

        var options = BuildOptions(connectionString);
        _databaseIndex = options.DefaultDatabase ?? 0;
        _mux = await ConnectionMultiplexer.ConnectAsync(options);
        _db = _mux.GetDatabase(_databaseIndex);

        var endpoint = _mux.GetEndPoints().FirstOrDefault()
            ?? throw new InvalidOperationException("Redis endpoint not found.");

        _server = _mux.GetServer(endpoint);
        _logger.LogInformation("Redis connection opened: {Endpoint}, db={Db}", endpoint, _databaseIndex);
    }

    public async Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            var options = BuildOptions(connectionString);
            await using var holder = new RedisConnectionHolder(await ConnectionMultiplexer.ConnectAsync(options));
            var db = holder.Mux.GetDatabase(options.DefaultDatabase ?? 0);
            await db.PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis connection test failed");
            return false;
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        await _db!.PingAsync();
    }

    public Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var config = _server!.ConfigGet("databases");
        var dbCount = config.FirstOrDefault().Value;
        var count = int.TryParse(dbCount, out var c) ? c : 16;
        var databases = Enumerable.Range(0, count).Select(i => $"db{i}").ToList();
        return Task.FromResult<IReadOnlyList<string>>(databases);
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var keys = new List<TableInfo>();
        await foreach (var key in EnumerateKeysAsync("*", ct))
            keys.Add(new TableInfo(Name: key));
        return keys.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var key = (RedisKey)table;
        var type = await _db!.KeyTypeAsync(key);

        return type switch
        {
            RedisType.Hash =>
            [
                new ColumnMeta("field", "string", IsNullable: false, Key: "PRI", OrdinalPosition: 0),
                new ColumnMeta("value", "string", IsNullable: true, OrdinalPosition: 1)
            ],
            RedisType.List =>
            [
                new ColumnMeta("index", "integer", IsNullable: false, Key: "PRI", OrdinalPosition: 0),
                new ColumnMeta("value", "string", IsNullable: true, OrdinalPosition: 1)
            ],
            RedisType.Set =>
            [
                new ColumnMeta("member", "string", IsNullable: false, Key: "PRI", OrdinalPosition: 0)
            ],
            RedisType.SortedSet =>
            [
                new ColumnMeta("member", "string", IsNullable: false, Key: "PRI", OrdinalPosition: 0),
                new ColumnMeta("score", "double", IsNullable: false, OrdinalPosition: 1)
            ],
            _ =>
            [
                new ColumnMeta("value", "string", IsNullable: true, Key: "PRI", OrdinalPosition: 0)
            ]
        };
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
            var parts = Tokenize(sql);
            if (parts.Count == 0)
                return ExecuteResult.FromError("Empty command.");

            var cmd = parts[0].ToUpperInvariant();
            return cmd switch
            {
                "PING" => await PingResultAsync(sw),
                "KEYS" => await KeysResultAsync(parts.ElementAtOrDefault(1) ?? "*", sw, ct),
                "GET" => await GetResultAsync(parts, sw),
                "TYPE" => await TypeResultAsync(parts, sw),
                "TTL" => await TtlResultAsync(parts, sw),
                "HGETALL" => await HashResultAsync(parts, sw),
                "DBSIZE" => await DbSizeResultAsync(sw),
                "INFO" => await InfoResultAsync(sw),
                "DEL" => ExecuteResult.FromAffected(await DelAsync(parts), sw.Elapsed),
                "EXPIRE" => ExecuteResult.FromAffected(await ExpireAsync(parts), sw.Elapsed),
                "RENAME" => ExecuteResult.FromAffected(await RenameAsync(parts), sw.Elapsed),
                "SET" => ExecuteResult.FromAffected(await SetAsync(parts), sw.Elapsed),
                "HSET" => ExecuteResult.FromAffected(await HashSetAsync(parts), sw.Elapsed),
                "SADD" => ExecuteResult.FromAffected(await SetAddAsync(parts), sw.Elapsed),
                "LPUSH" => ExecuteResult.FromAffected(await ListPushAsync(parts), sw.Elapsed),
                _ => ExecuteResult.FromError($"Unsupported Redis command: {cmd}")
            };
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
        var key = (RedisKey)table;
        var type = await _db!.KeyTypeAsync(key);

        var rows = new List<object?[]>();

        switch (type)
        {
            case RedisType.String:
            {
                var value = await _db.StringGetAsync(key);
                rows.Add([value.HasValue ? value.ToString() : string.Empty]);
                break;
            }
            case RedisType.Hash:
            {
                var entries = await _db.HashGetAllAsync(key);
                rows.AddRange(entries.Select(x => new object?[] { x.Name.ToString(), x.Value.ToString() }));
                break;
            }
            case RedisType.List:
            {
                var values = await _db.ListRangeAsync(key, 0, -1);
                rows.AddRange(values.Select((v, idx) => new object?[] { idx, v.ToString() }));
                break;
            }
            case RedisType.Set:
            {
                var members = await _db.SetMembersAsync(key);
                rows.AddRange(members.Select(m => new object?[] { m.ToString() }));
                break;
            }
            case RedisType.SortedSet:
            {
                var members = await _db.SortedSetRangeByRankWithScoresAsync(key, 0, -1);
                rows.AddRange(members.Select(m => new object?[] { m.Element.ToString(), m.Score }));
                break;
            }
        }

        var total = rows.Count;
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        var paged = rows.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToList();

        return new PagedResult<IReadOnlyList<object?[]>>(paged, total, safePage, safePageSize);
    }

    public async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var key = (RedisKey)table;

        if (values.Count == 1 && values.TryGetValue("value", out var value))
        {
            await _db!.StringSetAsync(key, value?.ToString() ?? string.Empty);
            return;
        }

        var entries = values.Select(kv => new HashEntry(kv.Key, kv.Value?.ToString() ?? string.Empty)).ToArray();
        if (entries.Length > 0)
            await _db!.HashSetAsync(key, entries);
    }

    public async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var key = (RedisKey)table;

        if (pkValues.TryGetValue("field", out var field) && newValues.TryGetValue("value", out var fieldValue))
        {
            await _db!.HashSetAsync(key, field?.ToString() ?? string.Empty, fieldValue?.ToString() ?? string.Empty);
            return;
        }

        if (newValues.TryGetValue("value", out var value))
        {
            await _db!.StringSetAsync(key, value?.ToString() ?? string.Empty);
            return;
        }

        var entries = newValues.Select(kv => new HashEntry(kv.Key, kv.Value?.ToString() ?? string.Empty)).ToArray();
        if (entries.Length > 0)
            await _db!.HashSetAsync(key, entries);
    }

    public async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var key = (RedisKey)table;

        if (pkValues.TryGetValue("field", out var field))
        {
            await _db!.HashDeleteAsync(key, field?.ToString() ?? string.Empty);
            return;
        }

        await _db!.KeyDeleteAsync(key);
    }

    public async Task CreateTableAsync(string ddl, CancellationToken ct = default)
    {
        EnsureConnected();
        var keyName = ddl.Trim();
        if (string.IsNullOrWhiteSpace(keyName))
            throw new InvalidOperationException("Redis create key command requires key name.");

        await _db!.StringSetAsync((RedisKey)keyName, string.Empty);
    }

    public Task DropTableAsync(string table, CancellationToken ct = default)
        => _db!.KeyDeleteAsync((RedisKey)table);

    public Task TruncateTableAsync(string table, CancellationToken ct = default)
        => _db!.KeyDeleteAsync((RedisKey)table);

    public Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
        => _db!.KeyRenameAsync((RedisKey)currentName, (RedisKey)newName);

    public async ValueTask DisposeAsync()
    {
        if (_mux is not null)
            await _mux.CloseAsync();

        _mux?.Dispose();
        _mux = null;
        _db = null;
        _server = null;
        _databaseIndex = 0;
    }

    private void EnsureConnected()
    {
        if (_mux is null || _db is null || _server is null)
            throw new InvalidOperationException("Redis connection is not open.");
    }

    private ConfigurationOptions BuildOptions(string connectionString)
    {
        if (connectionString.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
            return BuildOptionsFromUri(connectionString);

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return options;
    }

    private static ConfigurationOptions BuildOptionsFromUri(string connectionString)
    {
        var uri = new Uri(connectionString);
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            DefaultDatabase = 0
        };

        options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userParts = uri.UserInfo.Split(':');
            if (userParts.Length > 1)
                options.Password = Uri.UnescapeDataString(userParts[1]);
        }

        var path = uri.AbsolutePath.Trim('/');
        if (int.TryParse(path, out var db))
            options.DefaultDatabase = db;

        return options;
    }

    private async IAsyncEnumerable<string> EnumerateKeysAsync(string pattern, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        EnsureConnected();

        foreach (var key in _server!.Keys(_databaseIndex, pattern, pageSize: 1000))
        {
            ct.ThrowIfCancellationRequested();
            yield return key.ToString();
            await Task.Yield();
        }
    }

    private async Task<ExecuteResult> PingResultAsync(System.Diagnostics.Stopwatch sw)
    {
        var ping = await _db!.PingAsync();
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("ping_ms", "double", OrdinalPosition: 0)],
            [[Math.Round(ping.TotalMilliseconds)]],
            sw.Elapsed);
    }

    private async Task<ExecuteResult> KeysResultAsync(string pattern, System.Diagnostics.Stopwatch sw, CancellationToken ct)
    {
        var keys = new List<object?[]>();
        await foreach (var key in EnumerateKeysAsync(pattern, ct))
            keys.Add([key]);
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("key", "string", OrdinalPosition: 0)],
            keys, sw.Elapsed);
    }

    private async Task<ExecuteResult> GetResultAsync(IReadOnlyList<string> parts, System.Diagnostics.Stopwatch sw)
    {
        if (parts.Count < 2)
            return ExecuteResult.FromError("GET requires key argument.");
        var value = await _db!.StringGetAsync(parts[1]);
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("value", "string", OrdinalPosition: 0)],
            [[value.ToString()]], sw.Elapsed);
    }

    private async Task<ExecuteResult> TypeResultAsync(IReadOnlyList<string> parts, System.Diagnostics.Stopwatch sw)
    {
        if (parts.Count < 2)
            return ExecuteResult.FromError("TYPE requires key argument.");
        var type = await _db!.KeyTypeAsync(parts[1]);
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("type", "string", OrdinalPosition: 0)],
            [[type.ToString()]], sw.Elapsed);
    }

    private async Task<ExecuteResult> TtlResultAsync(IReadOnlyList<string> parts, System.Diagnostics.Stopwatch sw)
    {
        if (parts.Count < 2)
            return ExecuteResult.FromError("TTL requires key argument.");
        var ttl = await _db!.KeyTimeToLiveAsync(parts[1]);
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("ttl_seconds", "double", OrdinalPosition: 0)],
            [[ttl?.TotalSeconds]], sw.Elapsed);
    }

    private async Task<ExecuteResult> HashResultAsync(IReadOnlyList<string> parts, System.Diagnostics.Stopwatch sw)
    {
        if (parts.Count < 2)
            return ExecuteResult.FromError("HGETALL requires key argument.");
        var values = await _db!.HashGetAllAsync(parts[1]);
        sw.Stop();
        var rows = values.Select(v => new object?[] { v.Name.ToString(), v.Value.ToString() }).ToList();
        return ExecuteResult.FromRows(
            [new ColumnMeta("field", "string", OrdinalPosition: 0), new ColumnMeta("value", "string", OrdinalPosition: 1)],
            rows, sw.Elapsed);
    }

    private async Task<ExecuteResult> DbSizeResultAsync(System.Diagnostics.Stopwatch sw)
    {
        var count = await _server!.DatabaseSizeAsync(_databaseIndex);
        sw.Stop();
        return ExecuteResult.FromRows(
            [new ColumnMeta("dbsize", "long", OrdinalPosition: 0)],
            [[count]], sw.Elapsed);
    }

    private async Task<ExecuteResult> InfoResultAsync(System.Diagnostics.Stopwatch sw)
    {
        var info = await _server!.InfoAsync();
        sw.Stop();

        var rows = new List<object?[]>();
        foreach (var group in info)
        {
            foreach (var pair in group)
                rows.Add([group.Key, pair.Key, pair.Value]);
        }

        return ExecuteResult.FromRows(
            [
                new ColumnMeta("section", "string", OrdinalPosition: 0),
                new ColumnMeta("key", "string", OrdinalPosition: 1),
                new ColumnMeta("value", "string", OrdinalPosition: 2)
            ],
            rows, sw.Elapsed);
    }

    private async Task<int> DelAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 2)
            return 0;

        var keys = parts.Skip(1).Select(p => (RedisKey)p).ToArray();
        var deleted = await _db!.KeyDeleteAsync(keys);
        return (int)deleted;
    }

    private async Task<int> ExpireAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3 || !int.TryParse(parts[2], out var seconds))
            return 0;

        var ok = await _db!.KeyExpireAsync(parts[1], TimeSpan.FromSeconds(seconds));
        return ok ? 1 : 0;
    }

    private async Task<int> RenameAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3)
            return 0;

        var ok = await _db!.KeyRenameAsync(parts[1], parts[2]);
        return ok ? 1 : 0;
    }

    private async Task<int> SetAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3)
            return 0;

        var value = string.Join(' ', parts.Skip(2));
        var ok = await _db!.StringSetAsync(parts[1], value);
        return ok ? 1 : 0;
    }

    private async Task<int> HashSetAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 4)
            return 0;

        var added = await _db!.HashSetAsync(parts[1], parts[2], string.Join(' ', parts.Skip(3)));
        return added ? 1 : 0;
    }

    private async Task<int> SetAddAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3)
            return 0;

        var added = await _db!.SetAddAsync(parts[1], string.Join(' ', parts.Skip(2)));
        return added ? 1 : 0;
    }

    private async Task<int> ListPushAsync(IReadOnlyList<string> parts)
    {
        if (parts.Count < 3)
            return 0;

        var len = await _db!.ListLeftPushAsync(parts[1], string.Join(' ', parts.Skip(2)));
        return (int)len;
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        foreach (var ch in command)
        {
            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inSingle && !inDouble)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private sealed class RedisConnectionHolder : IAsyncDisposable
    {
        public ConnectionMultiplexer Mux { get; }

        public RedisConnectionHolder(ConnectionMultiplexer mux)
        {
            Mux = mux;
        }

        public async ValueTask DisposeAsync()
        {
            await Mux.CloseAsync();
            Mux.Dispose();
        }
    }
}
