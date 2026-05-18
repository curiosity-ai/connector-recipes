using System;
using System.IO;
using Amazon;
using Amazon.S3;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "S3 Sample (Subjects)";
var s3Bucket      = Environment.GetEnvironmentVariable("RECIPE_S3_BUCKET");
var s3Region      = Environment.GetEnvironmentVariable("RECIPE_S3_REGION") ?? "us-east-1";
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("S3Sample");

IObjectStore store;
if (!string.IsNullOrWhiteSpace(s3Bucket))
{
    var s3Client = new AmazonS3Client(RegionEndpoint.GetBySystemName(s3Region));
    store = new S3ObjectStore(s3Client, s3Bucket);
    logger.LogInformation("Reading from S3 bucket {Bucket} (region {Region})", s3Bucket, s3Region);
}
else
{
    store = new LocalObjectStore(localRoot);
    logger.LogInformation("Reading from local folder {Root}", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await SubjectsIngest.RegisterSchemaAsync(graph);
await SubjectsIngest.IngestAsync(graph, store);
await graph.CommitPendingAsync();

logger.LogInformation("Done.");
