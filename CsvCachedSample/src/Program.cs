using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "CSV Sample (Students, cached)";
var csvPath       = Environment.GetEnvironmentVariable("RECIPE_CSV_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "students.csv");
var cachePath     = Environment.GetEnvironmentVariable("RECIPE_CACHE_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "cache", "csv-students.db");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("CsvCachedSample");

var rows = CsvSource.Load<StudentsIngest.Row>(csvPath);
logger.LogInformation("Loaded {Count} rows from {Path}", rows.Count, csvPath);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

// HashCache is an on-disk LiteDB store of row fingerprints. On the second run
// against the same CSV every row hits the cache and the ingest call is skipped
// entirely — no graph writes, no network round-trips.
using var cache = HashCache.Initialize(cachePath);

await StudentsIngest.RegisterSchemaAsync(graph);

// Manual hit/miss accounting so the run logs how effective the cache is. The
// IfNotCached(...) convenience wrapper does the same thing in one call when
// you don't care about the counters.
var hits   = 0;
var misses = 0;
foreach (var row in rows)
{
    var hash = cache.Hash(row);
    if (cache.ContainsHash(StudentsIngest.ConnectorVersion, hash))
    {
        hits++;
        continue;
    }

    StudentsIngest.Ingest(graph, row);
    cache.EnqueueHash(StudentsIngest.ConnectorVersion, hash);
    misses++;
}

logger.LogInformation("HashCache: {Hits} hits, {Misses} misses", hits, misses);

// Commit ordering matters: persist to the graph first, then to the cache.
// If the graph commit throws, the in-memory hashes are discarded with the
// cache instance and the next run re-ingests the failed batch.
await graph.CommitPendingAsync();
await cache.CommitPendingHashesAsync();

logger.LogInformation("Done.");
