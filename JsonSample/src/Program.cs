using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "JSON Sample (Skills)";
var jsonPath      = Environment.GetEnvironmentVariable("RECIPE_JSON_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "skills.json");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("JsonSample");

var skills = JsonSource.LoadArray<SkillsIngest.SkillDoc>(jsonPath);
logger.LogInformation("Loaded {Count} skills from {Path}", skills.Count, jsonPath);

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await SkillsIngest.RegisterSchemaAsync(graph);
SkillsIngest.Ingest(graph, skills);
await graph.CommitPendingAsync();

logger.LogInformation("Done.");
