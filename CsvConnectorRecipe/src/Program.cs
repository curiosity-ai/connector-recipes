using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConnectorRecipes.Csv;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using static ConnectorRecipes.Csv.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "CSV Recipe (Students)";
var smoke         = Environment.GetEnvironmentVariable("RECIPE_SMOKE") == "1";
var maxRows       = int.TryParse(Environment.GetEnvironmentVariable("RECIPE_MAX_ROWS"), out var m) ? m : int.MaxValue;

var csvPath = FindFirstExisting(
    Environment.GetEnvironmentVariable("RECIPE_CSV_PATH"),
    Path.Combine("data", "students.csv"),
    Path.Combine("..", "data", "students.csv"),
    Path.Combine(AppContext.BaseDirectory, "data", "students.csv")
);

if (csvPath is null)
{
    Console.Error.WriteLine("Could not find data/students.csv. Set RECIPE_CSV_PATH to override.");
    return;
}

if (!smoke && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN. Set RECIPE_SMOKE=1 to dry-run without a workspace.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("CsvConnectorRecipe");

var rows = StudentsCsvLoader.Load(csvPath).Take(maxRows).ToList();
logger.LogInformation("Loaded {Count} rows from {Path}", rows.Count, csvPath);

if (smoke)
{
    foreach (var r in rows.Take(5))
    {
        Console.WriteLine($"--- {r.StudentId} {r.StudentName}");
        Console.WriteLine($"  University: {r.University} / {r.Department} ({r.Degree}, {r.Major})");
        Console.WriteLine($"  Advisor:    {r.AdvisorName} <{r.AdvisorEmail}>");
        Console.WriteLine($"  Location:   {r.City}, {r.Country}");
        Console.WriteLine($"  Subjects:   {string.Join(", ", r.Subjects)}");
        Console.WriteLine($"  Skills:     {string.Join(", ", r.Skills)}");
    }
    Console.WriteLine($"[smoke] {rows.Count} rows parsed; skipping graph upload.");
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken!, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

logger.LogInformation("Registering schema");
await CreateSchemasAsync(graph);

logger.LogInformation("Ingesting {Count} rows", rows.Count);
foreach (var row in rows)
{
    IngestRow(graph, row);
}

await graph.CommitPendingAsync();
logger.LogInformation("Commit complete; running counts");
await PrintCountsAsync(graph);

static string? FindFirstExisting(params string?[] candidates)
{
    foreach (var c in candidates)
    {
        if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
    }
    return null;
}

static async Task CreateSchemasAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<Nodes.Student>();
    await graph.CreateNodeSchemaAsync<Nodes.University>();
    await graph.CreateNodeSchemaAsync<Nodes.Department>();
    await graph.CreateNodeSchemaAsync<Nodes.Degree>();
    await graph.CreateNodeSchemaAsync<Nodes.Major>();
    await graph.CreateNodeSchemaAsync<Nodes.Subject>();
    await graph.CreateNodeSchemaAsync<Nodes.Skill>();
    await graph.CreateNodeSchemaAsync<Nodes.Advisor>();
    await graph.CreateNodeSchemaAsync<Nodes.Country>();
    await graph.CreateNodeSchemaAsync<Nodes.City>();
    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

static void IngestRow(Graph graph, StudentRow row)
{
    var studentNode = graph.AddOrUpdate(new Nodes.Student
    {
        Id             = row.StudentId,
        Name           = row.StudentName,
        BirthYear      = row.BirthYear,
        EnrollmentYear = row.EnrollmentYear,
        Gpa            = row.Gpa,
    });

    var universityNode = graph.TryAdd(new Nodes.University { Name = row.University });
    graph.Link(studentNode, universityNode, Edges.EnrolledAt, Edges.EnrolledStudent);

    var departmentNode = graph.TryAdd(new Nodes.Department
    {
        Id   = $"{row.University}/{row.Department}",
        Name = row.Department,
    });
    graph.Link(departmentNode, universityNode, Edges.PartOf, Edges.HasDepartment);
    graph.Link(studentNode, departmentNode, Edges.BelongsToDepartment, Edges.HasMember);

    var degreeNode = graph.TryAdd(new Nodes.Degree { Name = row.Degree });
    graph.Link(studentNode, degreeNode, Edges.PursuesDegree, Edges.PursuedBy);

    var majorNode = graph.TryAdd(new Nodes.Major { Name = row.Major });
    graph.Link(majorNode, departmentNode, Edges.OfferedBy, Edges.Offers);
    graph.Link(studentNode, majorNode, Edges.HasMajor, Edges.MajorOf);

    var advisorNode = graph.AddOrUpdate(new Nodes.Advisor
    {
        Email = row.AdvisorEmail,
        Name  = row.AdvisorName,
    });
    graph.Link(advisorNode, departmentNode, Edges.WorksIn, Edges.Employs);
    graph.Link(studentNode, advisorNode, Edges.AdvisedBy, Edges.Advises);

    foreach (var subject in row.Subjects)
    {
        var subjectNode = graph.TryAdd(new Nodes.Subject { Name = subject });
        graph.Link(studentNode, subjectNode, Edges.Studies, Edges.StudiedBy);
    }

    foreach (var skill in row.Skills)
    {
        var skillNode = graph.TryAdd(new Nodes.Skill { Name = skill });
        graph.Link(studentNode, skillNode, Edges.HasSkill, Edges.SkillOf);
    }

    var countryNode = graph.TryAdd(new Nodes.Country { Name = row.Country });
    var cityNode    = graph.TryAdd(new Nodes.City
    {
        Id   = $"{row.City}, {row.Country}",
        Name = row.City,
    });
    graph.Link(cityNode, countryNode, Edges.In, Edges.Includes);
    graph.Link(studentNode, cityNode, Edges.LivesIn, Edges.Resident);
}

static async Task PrintCountsAsync(Graph graph)
{
    var labels = new[]
    {
        nameof(Nodes.Student), nameof(Nodes.University), nameof(Nodes.Department),
        nameof(Nodes.Degree),  nameof(Nodes.Major),      nameof(Nodes.Subject),
        nameof(Nodes.Skill),   nameof(Nodes.Advisor),    nameof(Nodes.Country),
        nameof(Nodes.City),
    };

    Console.WriteLine("Node counts:");
    foreach (var label in labels)
    {
        var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
        Console.WriteLine($"  {label,-12} {resp.GetEmittedCount("C")}");
    }
}
