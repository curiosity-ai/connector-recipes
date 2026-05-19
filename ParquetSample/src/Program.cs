using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "Parquet Sample (Grades)";
var defaultPath   = Path.Combine(AppContext.BaseDirectory, "data", "grades.parquet");
var dataPath      = Environment.GetEnvironmentVariable("RECIPE_DATA_PATH") ?? defaultPath;

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

if (!File.Exists(dataPath))
{
    Console.Error.WriteLine(
        $"Data file not found at {dataPath}.\n" +
        $"Run scripts/generate_samples.py to create grades.parquet (and grades.avro).");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("ParquetSample");

IColumnarSource source = dataPath.EndsWith(".avro", StringComparison.OrdinalIgnoreCase)
    ? new AvroSource()
    : new ParquetSource();
logger.LogInformation("Reading {Path} with {Driver}", dataPath, source.GetType().Name);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await GradesIngest.RegisterSchemaAsync(graph);

var count = 0;
await foreach (var row in source.ReadAsync(dataPath, GradesIngest.Columns))
{
    GradesIngest.Ingest(graph, row);
    if (++count % 1000 == 0) await graph.CommitPendingAsync();
}

await graph.CommitPendingAsync();
logger.LogInformation("Ingested {Count} grade rows", count);
