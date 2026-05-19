using System;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "Sitemap Sample (University Pages)";
var sitemapUrl    = Environment.GetEnvironmentVariable("RECIPE_SITEMAP_URL");
var politenessMs  = int.TryParse(Environment.GetEnvironmentVariable("RECIPE_POLITENESS_MS"), out var pm) ? pm : 500;
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("SitemapSample");

ISitemapSource source;
string         sitemap;
if (!string.IsNullOrWhiteSpace(sitemapUrl))
{
    source  = new HttpSitemapSource(TimeSpan.FromMilliseconds(politenessMs), logger);
    sitemap = sitemapUrl;
    logger.LogInformation("Crawling sitemap at {Url} (politeness: {Ms} ms)", sitemapUrl, politenessMs);
}
else
{
    source  = new LocalSitemapSource(localRoot);
    sitemap = "sitemap.xml";
    logger.LogInformation("Reading from local folder {Root}", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await WebsiteIngest.RegisterSchemaAsync(graph);

var attempted = 0;
var ok        = 0;
var deduped   = 0;
var failed    = 0;

await foreach (var entry in source.ListUrlsAsync(sitemap))
{
    attempted++;
    var page = await source.FetchAsync(entry.Url);
    if (page is null) { deduped++; continue; }
    if (page.StatusCode >= 400) { failed++; continue; }

    WebsiteIngest.Ingest(graph, page, entry.LastModified);
    if (++ok % 25 == 0) await graph.CommitPendingAsync();
}

await graph.CommitPendingAsync();
logger.LogInformation("Sitemap: {Attempted} URLs, {Ok} pages ingested, {Deduped} duplicates, {Failed} failures",
    attempted, ok, deduped, failed);
(source as IDisposable)?.Dispose();
