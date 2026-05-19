using System;
using System.IO;
using System.Threading;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl   = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken       = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName  = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "Kafka Sample (Enrollment CDC)";
var bootstrap      = Environment.GetEnvironmentVariable("RECIPE_KAFKA_BOOTSTRAP");
var groupId        = Environment.GetEnvironmentVariable("RECIPE_KAFKA_GROUP")        ?? "curiosity-enrollments";
var topic          = Environment.GetEnvironmentVariable("RECIPE_KAFKA_TOPIC")        ?? "enrollments";
var maxIdleSeconds = int.TryParse(Environment.GetEnvironmentVariable("RECIPE_MAX_IDLE_SECONDS"), out var mis) ? mis : 5;
var eventsPath     = Environment.GetEnvironmentVariable("RECIPE_EVENTS_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "data", "events.jsonl");
var offsetPath     = Environment.GetEnvironmentVariable("RECIPE_OFFSET_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "data", ".offset");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("KafkaSample");

IEventStreamSource<EnrollmentsIngest.EnrollmentEvent> source;
if (!string.IsNullOrWhiteSpace(bootstrap))
{
    source = new KafkaEventStreamSource<EnrollmentsIngest.EnrollmentEvent>(bootstrap, groupId, topic, logger);
    logger.LogInformation("Reading from Kafka {Bootstrap} topic {Topic} (group {Group})", bootstrap, topic, groupId);
}
else
{
    source = new JsonlEventStreamSource<EnrollmentsIngest.EnrollmentEvent>(eventsPath, offsetPath);
    logger.LogInformation("Reading from local JSONL at {Path}", eventsPath);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await EnrollmentsIngest.RegisterSchemaAsync(graph);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var processed = 0;
StreamEvent<EnrollmentsIngest.EnrollmentEvent>? last = null;
DateTime lastEventTime = DateTime.UtcNow;

await foreach (var evt in source.ConsumeAsync(cts.Token))
{
    EnrollmentsIngest.Ingest(graph, evt.Value);
    last          = evt;
    lastEventTime = DateTime.UtcNow;

    if (++processed % 100 == 0)
    {
        await graph.CommitPendingAsync();
        source.CommitOffset(evt);
        logger.LogInformation("Committed batch ending at offset {Offset}", evt.Offset);
    }

    if (source is JsonlEventStreamSource<EnrollmentsIngest.EnrollmentEvent>) continue;
    if ((DateTime.UtcNow - lastEventTime).TotalSeconds > maxIdleSeconds) break;
}

await graph.CommitPendingAsync();
if (last is not null) source.CommitOffset(last);
source.Dispose();

logger.LogInformation("Done. Processed {Count} events.", processed);
