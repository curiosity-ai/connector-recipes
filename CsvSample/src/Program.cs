using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "CSV Sample (Students)";
var csvPath       = Environment.GetEnvironmentVariable("RECIPE_CSV_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "students.csv");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("CsvSample");

var rows = CsvSource.Load<StudentsIngest.Row>(csvPath);
logger.LogInformation("Loaded {Count} rows from {Path}", rows.Count, csvPath);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await StudentsIngest.RegisterSchemaAsync(graph);
foreach (var row in rows) StudentsIngest.Ingest(graph, row);
await graph.CommitPendingAsync();

logger.LogInformation("Done.");
