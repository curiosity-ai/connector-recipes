using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConnectorRecipes.UniversitiesSql;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using static ConnectorRecipes.UniversitiesSql.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "SQL Recipe (Universities)";
var smoke         = Environment.GetEnvironmentVariable("RECIPE_SMOKE") == "1";

var dbPath   = Environment.GetEnvironmentVariable("RECIPE_DB_PATH")
               ?? FindFirstWritablePath("data/universities.db", "../data/universities.db");
var seedPath = FindFirstExisting(
    Environment.GetEnvironmentVariable("RECIPE_SEED_SQL"),
    Path.Combine("data", "seed.sql"),
    Path.Combine("..", "data", "seed.sql"),
    Path.Combine(AppContext.BaseDirectory, "data", "seed.sql")
);

if (seedPath is null)
{
    Console.Error.WriteLine("Could not find data/seed.sql. Set RECIPE_SEED_SQL to override.");
    return;
}

if (!smoke && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN. Set RECIPE_SMOKE=1 to dry-run without a workspace.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("UniversitiesSqlConnectorRecipe");

logger.LogInformation("Ensuring SQLite DB at {Path} from seed {Seed}", dbPath, seedPath);
UniversitiesSqlLoader.EnsureSeeded(dbPath, seedPath);

var loader      = new UniversitiesSqlLoader(dbPath);
var universities = loader.LoadUniversities();
var departments  = loader.LoadDepartments();
var programs     = loader.LoadPrograms();
var faculty      = loader.LoadFaculty();

logger.LogInformation("Loaded {U} universities, {D} departments, {P} programs, {F} faculty",
    universities.Count, departments.Count, programs.Count, faculty.Count);

if (smoke)
{
    foreach (var u in universities.Take(3))
        Console.WriteLine($"--- University: {u.Name} ({u.Country}, founded {u.FoundedYear}, rank {u.Ranking})  {u.Website}");
    foreach (var d in departments.Take(3))
        Console.WriteLine($"--- Department: {d.University} / {d.Name}  head={d.HeadName}  research=[{string.Join(", ", d.ResearchAreas)}]");
    foreach (var f in faculty.Take(3))
        Console.WriteLine($"--- Faculty: {f.Name} <{f.Email}>  {f.Title}  h={f.HIndex}  research=[{string.Join(", ", f.ResearchAreas)}]");
    Console.WriteLine("[smoke] data loaded; skipping graph upload.");
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken!, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

logger.LogInformation("Registering schema");
await graph.CreateNodeSchemaAsync<Nodes.University>();
await graph.CreateNodeSchemaAsync<Nodes.Country>();
await graph.CreateNodeSchemaAsync<Nodes.Department>();
await graph.CreateNodeSchemaAsync<Nodes.Program>();
await graph.CreateNodeSchemaAsync<Nodes.Faculty>();
await graph.CreateNodeSchemaAsync<Nodes.ResearchArea>();
await graph.CreateEdgeSchemaAsync(typeof(Edges));

logger.LogInformation("Ingesting universities");
foreach (var u in universities)
{
    var universityNode = graph.AddOrUpdate(new Nodes.University
    {
        Name          = u.Name,
        FoundedYear   = u.FoundedYear,
        Ranking       = u.Ranking,
        StudentsCount = u.StudentsCount,
        Website       = u.Website,
    });

    var countryNode = graph.TryAdd(new Nodes.Country { Name = u.Country });
    graph.Link(universityNode, countryNode, Edges.BasedIn, Edges.Hosts);
}

logger.LogInformation("Ingesting departments");
var deptIdToKey = new System.Collections.Generic.Dictionary<int, string>();
foreach (var d in departments)
{
    var departmentKey = $"{d.University}/{d.Name}";
    deptIdToKey[d.Id] = departmentKey;

    var departmentNode = graph.AddOrUpdate(new Nodes.Department
    {
        Id        = departmentKey,
        Name      = d.Name,
        Building  = d.Building,
        HeadName  = d.HeadName,
        HeadEmail = d.HeadEmail,
    });

    graph.Link(departmentNode, Node.FromKey(nameof(Nodes.University), d.University), Edges.PartOf, Edges.HasDepartment);

    foreach (var area in d.ResearchAreas)
    {
        var areaNode = graph.TryAdd(new Nodes.ResearchArea { Name = area });
        graph.Link(departmentNode, areaNode, Edges.HasResearchArea, Edges.ResearchAreaOf);
    }
}

logger.LogInformation("Ingesting programs");
foreach (var p in programs)
{
    if (!deptIdToKey.TryGetValue(p.DepartmentId, out var departmentKey)) continue;

    var programKey = $"{departmentKey}/{p.Name}";
    var programNode = graph.AddOrUpdate(new Nodes.Program
    {
        Id            = programKey,
        Name          = p.Name,
        DegreeLevel   = p.DegreeLevel,
        DurationYears = p.DurationYears,
        Language      = p.Language,
        TuitionUsd    = p.TuitionUsd,
    });

    graph.Link(Node.FromKey(nameof(Nodes.Department), departmentKey), programNode, Edges.OffersProgram, Edges.ProgramOf);
}

logger.LogInformation("Ingesting faculty");
foreach (var f in faculty)
{
    if (!deptIdToKey.TryGetValue(f.DepartmentId, out var departmentKey)) continue;

    var facultyNode = graph.AddOrUpdate(new Nodes.Faculty
    {
        Email      = f.Email,
        Name       = f.Name,
        Title      = f.Title,
        HIndex     = f.HIndex,
        JoinedYear = f.JoinedYear,
    });

    graph.Link(facultyNode, Node.FromKey(nameof(Nodes.Department), departmentKey), Edges.TeachesIn, Edges.HasFaculty);

    foreach (var area in f.ResearchAreas)
    {
        var areaNode = graph.TryAdd(new Nodes.ResearchArea { Name = area });
        graph.Link(facultyNode, areaNode, Edges.Researches, Edges.ResearchedBy);
    }
}

await graph.CommitPendingAsync();
logger.LogInformation("Commit complete; running counts");

foreach (var label in new[] {
    nameof(Nodes.University), nameof(Nodes.Country), nameof(Nodes.Department),
    nameof(Nodes.Program),    nameof(Nodes.Faculty), nameof(Nodes.ResearchArea),
})
{
    var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
    Console.WriteLine($"  {label,-14} {resp.GetEmittedCount("C")}");
}

static string? FindFirstExisting(params string?[] candidates)
{
    foreach (var c in candidates)
    {
        if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
    }
    return null;
}

static string FindFirstWritablePath(params string[] candidates)
{
    foreach (var c in candidates)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(c));
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return c;
    }
    return candidates[0];
}
