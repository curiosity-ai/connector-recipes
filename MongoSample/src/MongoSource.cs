using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic document-store reader. The same async-enumerable
// of typed documents is produced whether the backing store is a real MongoDB
// instance or a local directory of JSON files — keeps ingestion code
// identical in both modes and makes the recipe runnable offline.
//
// MongoSource also exposes a change-stream helper for near-realtime
// incremental pulls; LocalMongoSource skips that (there's nothing changing
// to listen to).
public interface IMongoSource
{
    IAsyncEnumerable<T> StreamAsync<T>(string collection, CancellationToken ct = default);
    IAsyncEnumerable<T> StreamChangesAsync<T>(string collection, CancellationToken ct = default);
}

public sealed class MongoSource : IMongoSource
{
    private readonly IMongoDatabase _db;
    private readonly int            _batchSize;

    public MongoSource(string connectionString, string database, int batchSize = 500)
    {
        var client = new MongoClient(connectionString);
        _db        = client.GetDatabase(database);
        _batchSize = batchSize;
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(string collection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var coll   = _db.GetCollection<BsonDocument>(collection);
        using var cursor = await coll.FindAsync(FilterDefinition<BsonDocument>.Empty,
            new FindOptions<BsonDocument, BsonDocument> { BatchSize = _batchSize }, ct);

        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var doc in cursor.Current)
            {
                var json = doc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
                var typed = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (typed is not null) yield return typed;
            }
        }
    }

    public async IAsyncEnumerable<T> StreamChangesAsync<T>(string collection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var coll   = _db.GetCollection<BsonDocument>(collection);
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>();
        using var cursor = await coll.WatchAsync(pipeline,
            new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup }, ct);

        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var change in cursor.Current)
            {
                if (change.FullDocument is null) continue;
                var json  = change.FullDocument.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
                var typed = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (typed is not null) yield return typed;
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}

// Local fallback: each .json file under data/<collection>/ is one document.
// Reuse for offline development or for snapshots of a collection exported
// with `mongoexport --jsonArray`.
public sealed class LocalMongoSource : IMongoSource
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly string _root;

    public LocalMongoSource(string root) { _root = root; }

    public async IAsyncEnumerable<T> StreamAsync<T>(string collection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, collection);
        if (!Directory.Exists(dir)) yield break;

        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
            if (doc is not null) yield return doc;
        }
    }

#pragma warning disable CS1998 // intentionally empty enumerator
    public async IAsyncEnumerable<T> StreamChangesAsync<T>(string collection, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield break;
    }
#pragma warning restore CS1998
}
