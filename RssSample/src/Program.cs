using System;
using System.Collections.Generic;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "RSS Sample (University News)";
var feedUrlsCsv   = Environment.GetEnvironmentVariable("RECIPE_FEED_URLS");
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", "feeds");
var seenPath      = Environment.GetEnvironmentVariable("RECIPE_SEEN_PATH")
                    ?? Path.Combine(AppContext.BaseDirectory, "data", ".seen");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("RssSample");

// (feedSpec, displayName, universityName)
var feeds = new List<(string Spec, string Display, string? University)>();

if (!string.IsNullOrWhiteSpace(feedUrlsCsv))
{
    foreach (var url in feedUrlsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        feeds.Add((url.Trim(), url.Trim(), null));
}

IFeedSource source;
if (feeds.Count > 0)
{
    source = new HttpFeedSource();
    logger.LogInformation("Polling {N} remote feeds", feeds.Count);
}
else
{
    var local = new LocalFeedSource(localRoot);
    source    = local;

    feeds.Add(("mit-news.xml",      "MIT News",                       "MIT"));
    feeds.Add(("cmu-news.xml",      "Carnegie Mellon News",           "CMU"));
    feeds.Add(("stanford-news.atom","Stanford News",                  "Stanford"));
    feeds.Add(("berkeley-news.xml", "UC Berkeley News",               "Berkeley"));
    logger.LogInformation("Reading from local folder {Root}", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await NewsIngest.RegisterSchemaAsync(graph);

var seen     = new SeenEntryStore(seenPath);
var totalNew = 0;
var totalDup = 0;

foreach (var (spec, display, university) in feeds)
{
    var newInFeed = 0;
    var dupInFeed = 0;
    await foreach (var entry in source.ReadAsync(spec))
    {
        if (!seen.MarkNew(entry.EntryId)) { dupInFeed++; continue; }

        NewsIngest.Ingest(graph, entry, display, university);
        newInFeed++;
    }
    logger.LogInformation("{Feed}: {New} new, {Dup} already seen", display, newInFeed, dupInFeed);
    totalNew += newInFeed;
    totalDup += dupInFeed;
    await graph.CommitPendingAsync();
}

seen.Save();
(source as IDisposable)?.Dispose();
logger.LogInformation("Done. {New} new entries ingested, {Dup} duplicates skipped, {Total} entries seen total",
    totalNew, totalDup, seen.Count);
