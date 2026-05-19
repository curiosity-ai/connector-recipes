using System;
using System.IO;
using System.Threading;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl   = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken       = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName  = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "MongoDB Sample (Profiles)";
var mongoUri       = Environment.GetEnvironmentVariable("RECIPE_MONGO_URI");
var mongoDatabase  = Environment.GetEnvironmentVariable("RECIPE_MONGO_DB")           ?? "students";
var collectionName = Environment.GetEnvironmentVariable("RECIPE_MONGO_COLLECTION")   ?? "profiles";
var followChanges  = Environment.GetEnvironmentVariable("RECIPE_FOLLOW_CHANGES")     == "1";
var localRoot      = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                     ?? Path.Combine(AppContext.BaseDirectory, "data");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("MongoSample");

IMongoSource source;
if (!string.IsNullOrWhiteSpace(mongoUri))
{
    source = new MongoSource(mongoUri, mongoDatabase);
    logger.LogInformation("Reading from MongoDB at {Uri}, db {Db}, collection {Collection}",
        mongoUri, mongoDatabase, collectionName);
}
else
{
    source = new LocalMongoSource(localRoot);
    logger.LogInformation("Reading from local folder {Root}", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await ProfilesIngest.RegisterSchemaAsync(graph);

var initial = 0;
await foreach (var profile in source.StreamAsync<ProfilesIngest.ProfileDoc>(collectionName))
{
    ProfilesIngest.Ingest(graph, profile);
    initial++;
}
await graph.CommitPendingAsync();
logger.LogInformation("Ingested {Count} profiles from initial snapshot", initial);

if (followChanges && source is MongoSource liveSource)
{
    logger.LogInformation("Following change stream — Ctrl+C to stop");
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var changed = 0;
    try
    {
        await foreach (var profile in liveSource.StreamChangesAsync<ProfilesIngest.ProfileDoc>(collectionName, cts.Token))
        {
            ProfilesIngest.Ingest(graph, profile);
            if (++changed % 50 == 0) await graph.CommitPendingAsync();
        }
    }
    catch (OperationCanceledException) { }

    await graph.CommitPendingAsync();
    logger.LogInformation("Applied {Count} change-stream updates", changed);
}

logger.LogInformation("Done.");
