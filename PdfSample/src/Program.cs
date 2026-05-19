using System;
using System.IO;
using System.Text.Json;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "PDF Sample (Industrial Manuals)";
var docsRoot      = Environment.GetEnvironmentVariable("RECIPE_DOCS_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "manuals");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("PdfSample");

if (!Directory.Exists(docsRoot))
{
    logger.LogError("Document root {Root} does not exist. Run scripts/generate_samples.py first.", docsRoot);
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await ManualsIngest.RegisterSchemaAsync(graph);

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var count    = 0;
foreach (var doc in DocumentSource.Load(docsRoot))
{
    ManualsIngest.ManualMetadata? meta = null;
    if (doc.MetadataJson is not null)
        meta = JsonSerializer.Deserialize<ManualsIngest.ManualMetadata>(doc.MetadataJson, jsonOpts);

    ManualsIngest.Ingest(graph, doc, meta);
    if (++count % 5 == 0) await graph.CommitPendingAsync();
    logger.LogInformation("Ingested {File} ({Pages} pages)", doc.SourceFileName, doc.Pages.Count);
}

await graph.CommitPendingAsync();
logger.LogInformation("Ingested {Count} manuals", count);
