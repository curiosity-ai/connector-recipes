using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConnectorRecipes.SkillsJson;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using static ConnectorRecipes.SkillsJson.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "JSON Recipe (Skills)";
var smoke         = Environment.GetEnvironmentVariable("RECIPE_SMOKE") == "1";

var jsonPath = FindFirstExisting(
    Environment.GetEnvironmentVariable("RECIPE_JSON_PATH"),
    Path.Combine("data", "skills.json"),
    Path.Combine("..", "data", "skills.json"),
    Path.Combine(AppContext.BaseDirectory, "data", "skills.json")
);

if (jsonPath is null)
{
    Console.Error.WriteLine("Could not find data/skills.json. Set RECIPE_JSON_PATH to override.");
    return;
}

if (!smoke && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN. Set RECIPE_SMOKE=1 to dry-run without a workspace.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("SkillsJsonConnectorRecipe");

var skills = SkillsJsonLoader.Load(jsonPath);
logger.LogInformation("Loaded {Count} skills from {Path}", skills.Count, jsonPath);

if (smoke)
{
    foreach (var s in skills.Take(5))
    {
        Console.WriteLine($"--- {s.Name} ({s.Category}, {s.YearIntroduced})  popularity={s.Popularity}");
        Console.WriteLine($"  Prereqs:   {string.Join(", ", s.Prerequisites)}");
        Console.WriteLine($"  Related:   {string.Join(", ", s.Related)}");
        Console.WriteLine($"  Resources: {string.Join(", ", s.Resources.Select(r => $"{r.Title} ({r.Kind})"))}");
    }
    Console.WriteLine($"[smoke] {skills.Count} skills parsed; skipping graph upload.");
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken!, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

logger.LogInformation("Registering schema");
await graph.CreateNodeSchemaAsync<Nodes.Skill>();
await graph.CreateNodeSchemaAsync<Nodes.SkillCategory>();
await graph.CreateNodeSchemaAsync<Nodes.LearningResource>();
await graph.CreateEdgeSchemaAsync(typeof(Edges));

logger.LogInformation("Pass 1: nodes + categories + resources");
foreach (var s in skills)
{
    var skillNode = graph.AddOrUpdate(new Nodes.Skill
    {
        Name           = s.Name,
        Description    = s.Description,
        Popularity     = s.Popularity,
        YearIntroduced = s.YearIntroduced,
    });

    if (!string.IsNullOrWhiteSpace(s.Category))
    {
        var categoryNode = graph.TryAdd(new Nodes.SkillCategory { Name = s.Category });
        graph.Link(skillNode, categoryNode, Edges.HasCategory, Edges.CategoryOf);
    }

    foreach (var r in s.Resources)
    {
        if (string.IsNullOrWhiteSpace(r.Url)) continue;
        var resourceNode = graph.AddOrUpdate(new Nodes.LearningResource
        {
            Url   = r.Url,
            Title = r.Title,
            Kind  = r.Kind,
        });
        graph.Link(resourceNode, skillNode, Edges.Teaches, Edges.TaughtBy);
    }
}

// Pass 2 wires skill→skill edges by referencing nodes via Node.FromKey,
// so it doesn't matter whether the target was created earlier in this run.
logger.LogInformation("Pass 2: prerequisites + related-skill edges");
foreach (var s in skills)
{
    var src = Node.FromKey(nameof(Nodes.Skill), s.Name);

    foreach (var prereq in s.Prerequisites)
    {
        var dst = Node.FromKey(nameof(Nodes.Skill), prereq);
        graph.Link(src, dst, Edges.RequiresSkill, Edges.RequiredBy);
    }

    foreach (var related in s.Related)
    {
        var dst = Node.FromKey(nameof(Nodes.Skill), related);
        // Symmetric edge: same name in both directions.
        graph.Link(src, dst, Edges.RelatedToSkill, Edges.RelatedToSkill);
    }
}

await graph.CommitPendingAsync();
logger.LogInformation("Commit complete; running counts");

foreach (var label in new[] { nameof(Nodes.Skill), nameof(Nodes.SkillCategory), nameof(Nodes.LearningResource) })
{
    var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
    Console.WriteLine($"  {label,-20} {resp.GetEmittedCount("C")}");
}

static string? FindFirstExisting(params string?[] candidates)
{
    foreach (var c in candidates)
    {
        if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
    }
    return null;
}
