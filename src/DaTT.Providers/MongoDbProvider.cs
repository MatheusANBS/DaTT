using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Providers.Dialects;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DaTT.Providers;

public sealed class MongoDbProvider : IDatabaseProvider
{
    private MongoClient? _client;
    private IMongoDatabase? _database;
    private readonly ILogger<MongoDbProvider> _logger;

    public MongoDbProvider(ILogger<MongoDbProvider> logger)
    {
        _logger = logger;
    }

    public string EngineName => "MongoDB";
    public string[] SupportedSchemes => ["mongodb", "mongodb+srv"];
    public ISqlDialect Dialect => NoSqlDialect.Instance;
    public bool IsConnected => _client is not null && _database is not null;

    public Task<bool> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        try
        {
            var client = new MongoClient(connectionString);
            client.ListDatabaseNames(ct);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB connection test failed");
            return Task.FromResult(false);
        }
    }

    public Task ConnectAsync(string connectionString, CancellationToken ct = default)
    {
        var url = new MongoUrl(connectionString);
        _client = new MongoClient(url);
        var databaseName = url.DatabaseName ?? "admin";
        _database = _client.GetDatabase(databaseName);
        _logger.LogInformation("MongoDB connection opened to database '{Database}'", databaseName);
        return Task.CompletedTask;
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        await _database!.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
    }

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var cursor = await _client!.ListDatabaseNamesAsync(ct);
        return await cursor.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        var cursor = await _database!.ListCollectionNamesAsync(cancellationToken: ct);
        var names = await cursor.ToListAsync(ct);
        return names.Select(n => new TableInfo(Name: n)).ToList();
    }

    public Task<IReadOnlyList<TableInfo>> GetViewsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TableInfo>>([]);

    public Task<IReadOnlyList<string>> GetSchemasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public async Task<IReadOnlyList<ColumnMeta>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var collection = _database!.GetCollection<BsonDocument>(table);
        var sample = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (sample is null)
            return [];

        return sample.Elements.Select((el, i) => new ColumnMeta(
            Name: el.Name,
            DataType: el.Value.BsonType.ToString(),
            SimpleType: el.Value.BsonType.ToString(),
            Key: el.Name == "_id" ? "PRI" : null,
            IsNullable: el.Name != "_id",
            OrdinalPosition: i
        )).ToList();
    }

    public async Task<IReadOnlyList<IndexMeta>> GetIndexesAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        var collection = _database!.GetCollection<BsonDocument>(table);
        var cursor = await collection.Indexes.ListAsync(ct);
        var indexDocs = await cursor.ToListAsync(ct);

        return indexDocs.Select(doc => new IndexMeta(
            Name: doc.GetValue("name", "unknown").AsString,
            Columns: doc.GetValue("key", new BsonDocument()).AsBsonDocument.Names.ToList(),
            IsUnique: doc.GetValue("unique", false).ToBoolean(),
            IsPrimaryKey: doc.GetValue("name", "").AsString == "_id_"
        )).ToList();
    }

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

    public Task<ExecuteResult> ExecuteAsync(string json, CancellationToken ct = default)
        => Task.FromResult(ExecuteResult.FromError("Use GetRowsAsync for MongoDB queries."));

    public async Task<PagedResult<IReadOnlyList<object?[]>>> GetRowsAsync(
        string table, int page, int pageSize,
        string? filter = null, string? orderBy = null,
        CancellationToken ct = default)
    {
        EnsureConnected();
        var collection = _database!.GetCollection<BsonDocument>(table);
        var filterDef = string.IsNullOrWhiteSpace(filter)
            ? FilterDefinition<BsonDocument>.Empty
            : new JsonFilterDefinition<BsonDocument>(filter);

        var totalRows = (int)await collection.CountDocumentsAsync(filterDef, cancellationToken: ct);
        var docs = await collection.Find(filterDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var rows = docs.Select(d => d.Elements
            .Select(e => (object?)e.Value.ToString())
            .ToArray())
            .ToList();

        return new PagedResult<IReadOnlyList<object?[]>>(rows, totalRows, page, pageSize);
    }

    public async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureConnected();
        var doc = new BsonDocument(values.ToDictionary(kv => kv.Key, kv => BsonValue.Create(kv.Value)));
        await _database!.GetCollection<BsonDocument>(table).InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task UpdateRowAsync(string table, IReadOnlyDictionary<string, object?> newValues, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var filter = new BsonDocument(pkValues.ToDictionary(kv => kv.Key, kv => BsonValue.Create(kv.Value)));
        var update = new BsonDocument("$set", new BsonDocument(newValues.ToDictionary(kv => kv.Key, kv => BsonValue.Create(kv.Value))));
        await _database!.GetCollection<BsonDocument>(table)
            .UpdateOneAsync(new BsonDocumentFilterDefinition<BsonDocument>(filter),
                            new BsonDocumentUpdateDefinition<BsonDocument>(update),
                            cancellationToken: ct);
    }

    public async Task DeleteRowAsync(string table, IReadOnlyDictionary<string, object?> pkValues, CancellationToken ct = default)
    {
        EnsureConnected();
        var filter = new BsonDocument(pkValues.ToDictionary(kv => kv.Key, kv => BsonValue.Create(kv.Value)));
        await _database!.GetCollection<BsonDocument>(table)
            .DeleteOneAsync(new BsonDocumentFilterDefinition<BsonDocument>(filter), ct);
    }

    public async Task CreateTableAsync(string collectionName, CancellationToken ct = default)
    {
        EnsureConnected();
        await _database!.CreateCollectionAsync(collectionName, cancellationToken: ct);
    }

    public async Task DropTableAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        await _database!.DropCollectionAsync(table, ct);
    }

    public Task TruncateTableAsync(string table, CancellationToken ct = default)
    {
        EnsureConnected();
        return _database!.GetCollection<BsonDocument>(table)
            .DeleteManyAsync(FilterDefinition<BsonDocument>.Empty, ct)
            .ContinueWith(_ => { }, ct);
    }

    public async Task RenameTableAsync(string currentName, string newName, CancellationToken ct = default)
    {
        EnsureConnected();
        var adminDb = _client!.GetDatabase("admin");
        var dbName = _database!.DatabaseNamespace.DatabaseName;
        var command = new BsonDocument
        {
            { "renameCollection", $"{dbName}.{currentName}" },
            { "to", $"{dbName}.{newName}" }
        };
        await adminDb.RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
    }

    private void EnsureConnected()
    {
        if (_client is null || _database is null)
            throw new InvalidOperationException("MongoDB connection is not open.");
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        _database = null;
        return ValueTask.CompletedTask;
    }
}
