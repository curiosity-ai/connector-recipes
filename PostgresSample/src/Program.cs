using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "PostgreSQL Sample (Grants)";
var dbUrl         = Environment.GetEnvironmentVariable("RECIPE_DB_URL");
var pageSize      = int.TryParse(Environment.GetEnvironmentVariable("RECIPE_PAGE_SIZE"), out var ps) ? ps : 1000;
var watermarkPath = Environment.GetEnvironmentVariable("RECIPE_WATERMARK_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", ".watermark");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

if (string.IsNullOrWhiteSpace(dbUrl))
{
    Console.Error.WriteLine(
        "Missing RECIPE_DB_URL. Examples:\n" +
        "  postgres://user:pass@localhost:5432/grants_db\n" +
        "  mysql://user:pass@localhost:3306/grants_db\n" +
        "\nSee data/docker-compose.yml + data/seed.sql to spin up a local copy.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("PostgresSample");

var db   = SqlServerSource.FromUrl(dbUrl, logger);
var mark = new SqlServerSource.Watermark(watermarkPath);
var startKey = mark.Read(fallback: "1970-01-01T00:00:00Z");
logger.LogInformation("Resuming from updated_at > {Watermark}", startKey);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await GrantsIngest.RegisterSchemaAsync(graph);

var count    = 0;
var lastSeen = startKey;
foreach (var row in db.StreamPaged(
             GrantsIngest.PagedSql,
             startKey,
             GrantsIngest.Map,
             keyOf: r => r.UpdatedAt.ToString("o"),
             pageSize: pageSize))
{
    GrantsIngest.Ingest(graph, row);
    lastSeen = row.UpdatedAt.ToString("o");
    if (++count % 500 == 0) await graph.CommitPendingAsync();
}

await graph.CommitPendingAsync();
mark.Write(lastSeen);
logger.LogInformation("Ingested {Count} grants; watermark advanced to {Watermark}", count, lastSeen);
