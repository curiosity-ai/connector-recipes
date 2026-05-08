using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using ConnectorRecipes.SubjectsS3;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using static ConnectorRecipes.SubjectsS3.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "S3 Recipe (Subjects)";
var smoke         = Environment.GetEnvironmentVariable("RECIPE_SMOKE") == "1";

var s3Bucket      = Environment.GetEnvironmentVariable("RECIPE_S3_BUCKET");
var s3Region      = Environment.GetEnvironmentVariable("RECIPE_S3_REGION") ?? "us-east-1";
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? FindFirstExistingDir(
                           Path.Combine("data"),
                           Path.Combine("..", "data"),
                           Path.Combine(AppContext.BaseDirectory, "data"));

if (!smoke && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN. Set RECIPE_SMOKE=1 to dry-run without a workspace.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("SubjectsS3ConnectorRecipe");

ISubjectsSource source;
if (!string.IsNullOrWhiteSpace(s3Bucket))
{
    var s3Client = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(s3Region));
    source = new S3SubjectsSource(s3Client, s3Bucket);
    logger.LogInformation("Reading from S3 bucket {Bucket} (region {Region})", s3Bucket, s3Region);
}
else
{
    if (string.IsNullOrWhiteSpace(localRoot))
    {
        Console.Error.WriteLine("Could not find local data/ folder. Set RECIPE_LOCAL_ROOT or RECIPE_S3_BUCKET.");
        return;
    }
    source = new LocalSubjectsSource(localRoot);
    logger.LogInformation("Reading from local fallback root {Root}", localRoot);
}

var subjects = await ReadAllAsync<SubjectJson>(source, "subjects/");
var books    = await ReadAllAsync<BookJson>   (source, "books/");
logger.LogInformation("Loaded {Subjects} subjects and {Books} books", subjects.Count, books.Count);

if (smoke)
{
    foreach (var s in subjects.Take(5))
    {
        Console.WriteLine($"--- {s.Name} ({s.Level})");
        Console.WriteLine($"  Topics: {string.Join(", ", s.Topics)}");
        Console.WriteLine($"  Books:  {string.Join(", ", s.BookIsbns)}");
    }
    Console.WriteLine($"[smoke] {subjects.Count} subjects + {books.Count} books parsed; skipping graph upload.");
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken!, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

logger.LogInformation("Registering schema");
await graph.CreateNodeSchemaAsync<Nodes.Subject>();
await graph.CreateNodeSchemaAsync<Nodes.Topic>();
await graph.CreateNodeSchemaAsync<Nodes.Book>();
await graph.CreateNodeSchemaAsync<Nodes.Author>();
await graph.CreateEdgeSchemaAsync(typeof(Edges));

var booksByIsbn = books.ToDictionary(b => b.Isbn, b => b);

logger.LogInformation("Ingesting subjects");
foreach (var s in subjects)
{
    var subjectNode = graph.AddOrUpdate(new Nodes.Subject
    {
        Name        = s.Name,
        Level       = s.Level,
        Description = s.Description,
    });

    foreach (var topic in s.Topics)
    {
        var topicNode = graph.TryAdd(new Nodes.Topic { Name = topic });
        graph.Link(subjectNode, topicNode, Edges.Covers, Edges.CoveredIn);
    }

    foreach (var isbn in s.BookIsbns)
    {
        if (!booksByIsbn.TryGetValue(isbn, out var book))
        {
            logger.LogWarning("Subject {Subject} references unknown ISBN {Isbn}", s.Name, isbn);
            continue;
        }

        var bookNode = graph.AddOrUpdate(new Nodes.Book
        {
            Isbn    = book.Isbn,
            Title   = book.Title,
            Year    = book.Year,
            Edition = book.Edition,
        });
        graph.Link(subjectNode, bookNode, Edges.RecommendsBook, Edges.RecommendedFor);

        foreach (var author in book.Authors)
        {
            var authorNode = graph.TryAdd(new Nodes.Author { Name = author });
            graph.Link(bookNode, authorNode, Edges.WrittenBy, Edges.Wrote);
        }
    }
}

await graph.CommitPendingAsync();
logger.LogInformation("Commit complete; running counts");

foreach (var label in new[] { nameof(Nodes.Subject), nameof(Nodes.Topic), nameof(Nodes.Book), nameof(Nodes.Author) })
{
    var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
    Console.WriteLine($"  {label,-10} {resp.GetEmittedCount("C")}");
}

static async Task<List<T>> ReadAllAsync<T>(ISubjectsSource source, string prefix)
{
    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var list = new List<T>();
    await foreach (var (_, json) in source.ListAsync(prefix))
    {
        var item = JsonSerializer.Deserialize<T>(json, opts);
        if (item is not null) list.Add(item);
    }
    return list;
}

static string? FindFirstExistingDir(params string[] candidates)
{
    foreach (var c in candidates)
    {
        if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c)) return c;
    }
    return null;
}
