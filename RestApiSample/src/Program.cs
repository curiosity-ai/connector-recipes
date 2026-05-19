using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "REST API Sample (Courses)";
var apiBaseUrl    = Environment.GetEnvironmentVariable("RECIPE_API_BASE_URL");
var apiBearer     = Environment.GetEnvironmentVariable("RECIPE_API_TOKEN");
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("RestApiSample");

IRestApiSource source;
if (!string.IsNullOrWhiteSpace(apiBaseUrl))
{
    source = new HttpRestApiSource(apiBaseUrl, apiBearer);
    logger.LogInformation("Reading from REST API at {Url}", apiBaseUrl);
}
else
{
    source = new LocalRestApiSource(localRoot);
    logger.LogInformation("Reading from local folder {Root}", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await CoursesIngest.RegisterSchemaAsync(graph);

var count = 0;
await foreach (var course in source.StreamAsync<CoursesIngest.CourseDoc>("courses"))
{
    CoursesIngest.Ingest(graph, course);
    count++;
}
logger.LogInformation("Ingested {Count} courses", count);

await graph.CommitPendingAsync();
(source as IDisposable)?.Dispose();

logger.LogInformation("Done.");
