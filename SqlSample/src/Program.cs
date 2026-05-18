using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "SQL Sample (Universities)";
var dbPath        = Environment.GetEnvironmentVariable("RECIPE_DB_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "universities.db");
var seedPath      = Environment.GetEnvironmentVariable("RECIPE_SEED_SQL")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "seed.sql");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("SqlSample");

SqliteSource.EnsureSeeded(dbPath, seedPath);
var db = new SqliteSource(dbPath);
logger.LogInformation("Reading from SQLite at {Path}", dbPath);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await UniversitiesIngest.RegisterSchemaAsync(graph);
UniversitiesIngest.Ingest(graph, db);
await graph.CommitPendingAsync();

logger.LogInformation("Done.");
