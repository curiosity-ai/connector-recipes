# Connector Recipes

A library of self-contained, runnable examples for ingesting common source formats — CSV, JSON, S3, REST APIs, SQL — into a [Curiosity](https://curiosity.ai) knowledge-graph workspace.

Each recipe is a small .NET console app you can copy, adapt, and run against your own workspace. Together they form a practical reference for the `Curiosity.Library` SDK: schema definition, ingestion, edge modeling, and verification queries.

---

## Table of contents

1. [What is a data connector?](#what-is-a-data-connector)
2. [Why write one?](#why-write-one)
3. [Prerequisites](#prerequisites)
4. [Project setup](#project-setup)
5. [Designing your schema](#designing-your-schema)
6. [Modeling nodes and edges](#modeling-nodes-and-edges)
7. [Connecting to a workspace](#connecting-to-a-workspace)
8. [Ingestion patterns](#ingestion-patterns)
9. [Source-format quick-starts](#source-format-quick-starts)
10. [Verifying the result with queries](#verifying-the-result-with-queries)
11. [Re-running safely (idempotency)](#re-running-safely-idempotency)
12. [Common pitfalls](#common-pitfalls)
13. [End-to-end example](#end-to-end-example)

---

## What is a data connector?

A **data connector** is a small program that reads data from a source — a file, an API, a database — and writes it into a Curiosity workspace as a **knowledge graph**: a network of typed **nodes** (entities like *Customer*, *Product*, *SupportCase*) connected by typed **edges** (relationships like *Purchased*, *AssignedTo*, *DependsOn*).

Once your data lives in the graph, the workspace gives you:

- Typed search and filtering across all entity types at once.
- Visual graph exploration (click a node, see its neighbors).
- A query language for traversal, aggregation, and ad-hoc analysis.
- Hooks for natural-language search, embeddings, and conversational interfaces.

A connector is the bridge from **your data** to **that experience**.

## Why write one?

Most useful when:

- Your data is **relational by nature** but lives in formats that make those relationships hard to follow (CSVs, log files, REST endpoints, scattered JSON exports).
- You want to **unify multiple sources** behind one searchable interface — e.g. customers from a CRM, tickets from a support system, devices from an inventory database, all linked.
- You want **non-technical users to explore the data** without writing SQL or building dashboards.
- You're prototyping a knowledge-graph application and need real data in fast.

A connector is **not** a streaming pipeline replacement. It's an ingestion job — run it on a schedule (cron, CI, a workflow runner) or on demand.

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report `10.x`).
- A **Curiosity workspace** you can connect to. Local workspaces typically run at `http://localhost:8080/`; hosted workspaces have their own URL.
- An **API token** from that workspace. Generate one in the workspace UI:
  `Manage → API integrations → Create API Token`. Copy the token value — it's shown only once.

That's it. Everything else comes from NuGet.

## Project setup

Create a new console app and add the `Curiosity.Library` package:

```bash
dotnet new console -n MyConnector
cd MyConnector
dotnet add package Curiosity.Library
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.Console
```

Then add whichever packages your source format needs — `CsvHelper`, `Newtonsoft.Json`, `AWSSDK.S3`, `Npgsql`, etc.

A finished `.csproj` looks like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>MyConnector</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Curiosity.Library" Version="26.4.469" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.6" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.6" />
    <!-- format-specific packages -->
  </ItemGroup>
</Project>
```

Recommended folder layout for anything beyond the smallest recipe:

```
MyConnector/
├── MyConnector.csproj
├── data/                    # sample inputs you ship with the recipe
└── src/
    ├── Program.cs           # entry point
    ├── Schema.cs            # node + edge type definitions
    ├── nodes/               # one file per node type (optional)
    └── ingestion/           # source-specific reader + parser
```

## Designing your schema

Before you write ingestion code, sketch the graph you want. Pick the **entities** that matter (these become node types) and the **relationships** between them (these become edge types).

A good rule of thumb: **if you'd ever want to filter, group, or navigate by it, it should be a node**, not just a property.

A field like `Status = "Open"` repeated across thousands of support cases is much more useful as a `Status` node connected by a `HasStatus` edge — you can then filter all cases by status, or see all entities (across types) that share a status, with one query.

Example schema for a customer-support dataset:

| Node type | Key | Notes |
|---|---|---|
| `Customer` | `Email` | One per unique email. |
| `Device` | `SerialNumber` | Hardware the case is about. |
| `SupportCase` | `Id` | The case itself; carries Summary, Body, OpenedAt. |
| `Status` | `Value` | Promoted from a string field on `SupportCase`. |
| `Agent` | `Username` | Who handled the case. |

| Edge type | Direction | Reverse name |
|---|---|---|
| `Owns` | Customer → Device | `OwnedBy` |
| `RaisedBy` | SupportCase → Customer | `Raised` |
| `ForDevice` | SupportCase → Device | `HasSupportCase` |
| `HasStatus` | SupportCase → Status | `StatusOf` |
| `AssignedTo` | SupportCase → Agent | `Handles` |

Always define edges **in pairs** — a forward name and a reverse name — so traversal works in both directions.

## Modeling nodes and edges

Schemas are plain C# classes decorated with attributes from `Curiosity.Library`. There are four:

| Attribute | Where it goes | Meaning |
|---|---|---|
| `[Node]` | On a class | Marks the class as a graph node type. |
| `[Key]` | On one property per `[Node]` | The unique identity of the node. Inserting twice with the same key updates the same node. |
| `[Property]` | On a property | A regular indexed field — searchable, filterable. |
| `[Timestamp]` | On a `DateTimeOffset` property | Special index for time-based sorting and range queries. |

Edges are simpler: a static class of `string` constants. The names are what the graph stores, so use `nameof(...)` so renames stay in sync.

```csharp
using System;
using Curiosity.Library;

namespace MyConnector;

public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Customer
        {
            [Key]      public string Email { get; set; } = string.Empty;
            [Property] public string Name  { get; set; } = string.Empty;
        }

        [Node]
        public class Device
        {
            [Key]      public string SerialNumber { get; set; } = string.Empty;
            [Property] public string Model        { get; set; } = string.Empty;
        }

        [Node]
        public class SupportCase
        {
            [Key]       public string Id      { get; set; } = string.Empty;
            [Property]  public string Summary { get; set; } = string.Empty;
            [Property]  public string Body    { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset OpenedAt { get; set; }
        }

        [Node]
        public class Status
        {
            [Key] public string Value { get; set; } = string.Empty;
        }

        [Node]
        public class Agent
        {
            [Key]      public string Username { get; set; } = string.Empty;
            [Property] public string FullName { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string Owns           = nameof(Owns);
        public const string OwnedBy        = nameof(OwnedBy);
        public const string RaisedBy       = nameof(RaisedBy);
        public const string Raised         = nameof(Raised);
        public const string ForDevice      = nameof(ForDevice);
        public const string HasSupportCase = nameof(HasSupportCase);
        public const string HasStatus      = nameof(HasStatus);
        public const string StatusOf       = nameof(StatusOf);
        public const string AssignedTo     = nameof(AssignedTo);
        public const string Handles        = nameof(Handles);
    }
}
```

### Splitting large schemas across files

For more than a handful of node types, declare the wrapper as `partial` and put each node in its own file. Diffs stay readable and a single change doesn't churn one giant `Schema.cs`.

```csharp
// Schema.cs
public static partial class Schema
{
    public static partial class Nodes { }
    public static class Edges { /* edge constants */ }
}

// nodes/Customer.cs
public static partial class Schema
{
    public static partial class Nodes
    {
        [Node]
        public class Customer
        {
            [Key] public string Email { get; set; } = string.Empty;
        }
    }
}
```

## Connecting to a workspace

`Graph.Connect(url, token, connectorName)` returns a disposable `Graph` handle. The connector name shows up in the workspace UI so administrators can see which job inserted which data.

Read configuration from environment variables — never hard-code tokens.

```csharp
using System;
using Curiosity.Library;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN")
                     ?? throw new InvalidOperationException("Set CURIOSITY_API_TOKEN.");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "My Connector";

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName)
                       .WithLoggingFactory(loggerFactory);

// stream server-side logs back through the local logger
loggerFactory.AddProvider(graph.GetServerLoggingProvider());
```

Once you have a `graph`, register every node type and the edge class **before** inserting anything:

```csharp
await graph.CreateNodeSchemaAsync<Schema.Nodes.Customer>();
await graph.CreateNodeSchemaAsync<Schema.Nodes.Device>();
await graph.CreateNodeSchemaAsync<Schema.Nodes.SupportCase>();
await graph.CreateNodeSchemaAsync<Schema.Nodes.Status>();
await graph.CreateNodeSchemaAsync<Schema.Nodes.Agent>();
await graph.CreateEdgeSchemaAsync(typeof(Schema.Edges));
```

Schema registration is idempotent — calling it on an existing schema is a no-op for unchanged definitions and adds new properties when present.

## Ingestion patterns

The graph API is small. These four calls cover most of what a connector does.

### Insert (or fetch) a node

```csharp
var customer = graph.TryAdd(new Schema.Nodes.Customer
{
    Email = "alice@example.com",
    Name  = "Alice"
});
```

`TryAdd` is idempotent: if a `Customer` with that `Email` already exists, the existing node is returned and properties are left alone.

### Insert or update properties

```csharp
var supportCase = graph.AddOrUpdate(new Schema.Nodes.SupportCase
{
    Id       = "SC-0001",
    Summary  = "Battery drains quickly",
    Body     = "...",
    OpenedAt = DateTimeOffset.UtcNow
});
```

Use `AddOrUpdate` when re-running the connector should refresh property values (e.g. a case's body has been edited upstream).

### Link two nodes (always supply both directions)

```csharp
graph.Link(supportCase, customer, Schema.Edges.RaisedBy, Schema.Edges.Raised);
graph.Link(supportCase, device,   Schema.Edges.ForDevice, Schema.Edges.HasSupportCase);
```

The third and fourth arguments are the **forward** and **reverse** edge type names. Always supply both — single-direction edges look fine in spot checks but break every reverse traversal.

### Link to a node by key when you don't have a handle

```csharp
graph.Link(
    supportCase,
    Node.FromKey(nameof(Schema.Nodes.Agent), "agent-42"),
    Schema.Edges.AssignedTo,
    Schema.Edges.Handles);
```

Useful when the target node is created by a different pass through the data, or by a different connector entirely.

### Replace edges of one type

If a field can change over time (status, owner, category) and you don't want stale edges piling up on re-run, unlink the old edge of that type before adding the new one:

```csharp
var status = graph.TryAdd(new Schema.Nodes.Status { Value = "Open" });
graph.UnlinkExcept(supportCase, status, Schema.Edges.HasStatus, Schema.Edges.StatusOf);
graph.Link(supportCase, status, Schema.Edges.HasStatus, Schema.Edges.StatusOf);
```

### Commit pending operations

`TryAdd` / `AddOrUpdate` / `Link` queue operations locally. Flush them with:

```csharp
await graph.CommitPendingAsync();
```

The `using` block on `graph` will also flush on dispose, but commit explicitly at logical boundaries. For large jobs, commit in batches so you don't build an unbounded in-memory queue:

```csharp
int i = 0;
foreach (var row in rows)
{
    /* TryAdd / Link calls */
    if (++i % 1000 == 0) await graph.CommitPendingAsync();
}
await graph.CommitPendingAsync();
```

### Parallel ingestion

`TryAdd` and `Link` are thread-safe, so `Parallel.ForEachAsync` is fine. The one thing to watch for: shared entities. If two threads call `graph.TryAdd(new Manufacturer { Name = "Acme" })` simultaneously you'll issue redundant operations. Wrap the lookup in a `ConcurrentDictionary`:

```csharp
var manufacturers = new ConcurrentDictionary<string, Node>();

await Parallel.ForEachAsync(rows, async (row, _) =>
{
    var mfg  = manufacturers.GetOrAdd(row.Manufacturer,
        m => graph.TryAdd(new Schema.Nodes.Manufacturer { Name = m }));
    var part = graph.TryAdd(new Schema.Nodes.Part { Name = row.PartName });
    graph.Link(part, mfg, Schema.Edges.HasManufacturer, Schema.Edges.ManufacturerOf);
});

await graph.CommitPendingAsync();
```

### Aliases (improve search hit rate)

A node is searchable by its `[Key]` and `[Property]` fields by default. If users will look it up by alternate spellings — abbreviations, hyphenated forms, foreign-language names — add aliases:

```csharp
graph.AddAlias(deviceNode, Mosaik.Core.Language.Any, "iPhone 12", ignoreCase: false);
graph.AddAlias(deviceNode, Mosaik.Core.Language.Any, "iPhone-12", ignoreCase: false);
```

`Mosaik.Core.Language.Any` applies the alias regardless of UI language. Pass a specific language for localized variants.

## Source-format quick-starts

The patterns below assume your `IngestAsync` method receives a `Graph` and an `IEnumerable<TRow>` of source rows. The reader (CSV/JSON/S3/etc.) is the only thing that changes between formats.

### CSV (`CsvHelper`)

```csharp
using System.Globalization;
using CsvHelper;

public static IEnumerable<MyRow> Load(string path)
{
    using var reader = new StreamReader(path);
    using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
    foreach (var row in csv.GetRecords<MyRow>())
        yield return row;
}
```

Map columns explicitly with `[Name("Header Name")]` if the CSV header doesn't match your C# property — silent mismatches are the most common cause of an unexpectedly empty graph.

### JSON

```csharp
using System.Text.Json;

var items = JsonSerializer.Deserialize<MyJson[]>(File.ReadAllText(path))!;
```

For very large arrays, prefer the streaming `Utf8JsonReader` so you don't load the whole file into memory.

### S3 (`AWSSDK.S3`)

```csharp
var s3   = new AmazonS3Client();   // picks up creds from env / instance profile
var list = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix });

foreach (var obj in list.S3Objects)
{
    using var resp   = await s3.GetObjectAsync(bucket, obj.Key);
    using var stream = resp.ResponseStream;
    /* parse stream → emit nodes/edges */
}
```

Stream the response — never `ReadToEndAsync` large objects in a loop.

### REST API

```csharp
using var http = new HttpClient { BaseAddress = new Uri(apiBase) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

var page = await http.GetFromJsonAsync<PagedResponse<Item>>($"items?page={page}");
```

Honor `Retry-After` and back off on `429`. For paginated endpoints, fetch lazily inside an `IAsyncEnumerable<T>` so you don't materialize the whole result set.

### SQL (`Npgsql`, `Microsoft.Data.SqlClient`, etc.)

Read in batches with keyset pagination — never load an entire large table into memory:

```sql
SELECT id, ... FROM customers WHERE id > @lastId ORDER BY id LIMIT 1000;
```

## Verifying the result with queries

`Curiosity.Library` exposes a fluent query language. From inside the connector, build a query through `graph.QueryAsync(q => …)`:

```csharp
// count
var resp  = await graph.QueryAsync(q => q.StartAt(nameof(Schema.Nodes.Customer)).EmitCount("C"));
var count = resp.GetEmittedCount("C");
Console.WriteLine($"Customers: {count}");

// fetch some nodes with selected properties
var resp2 = await graph.QueryAsync(q => q
    .StartAt(nameof(Schema.Nodes.Customer))
    .Take(10)
    .Emit("N", [nameof(Schema.Nodes.Customer.Name)]));

foreach (var node in resp2.GetEmitted("N"))
    Console.WriteLine(node.GetField<string>(nameof(Schema.Nodes.Customer.Name)));
```

The same query language is available **inside the workspace** via the admin Shell — useful for ad-hoc exploration after ingestion. Open `Manage → Shell` and paste:

```csharp
// 10 customers
return Q().StartAt(N.Customer.Type).Take(10).Emit("N");

// support cases for a specific customer
return Q().StartAt(N.Customer.Type, "alice@example.com")
          .Out(N.SupportCase.Type)
          .Emit("N");

// newest cases first
return Q().StartAt(N.SupportCase.Type)
          .SortByTimestamp(oldestFirst: false)
          .Take(20)
          .Emit("N");

// summary of relationships around a node type — best smoke test
return Q().StartAt(N.SupportCase.Type).EmitNeighborsSummary();

// the whole graph, summarized
return Q().EmitNeighborsSummary();

// filter on properties
return Q().StartAt(N.Device.Type)
          .Where(n => n.GetString(N.Device.Model).Contains("iPhone"))
          .Emit("N");
```

Common chain segments:

| Method | Purpose |
|---|---|
| `.StartAt(type)` / `.StartAt(type, key)` | Anchor traversal at all nodes of a type, or one specific node. |
| `.Out(type, edgeType?)` / `.In(type, edgeType?)` | Walk an outgoing or incoming edge. |
| `.Where(n => …)` | Filter on properties. |
| `.Take(n)` / `.Skip(n)` | Limit / paginate. |
| `.SortByTimestamp(oldestFirst)` | Order by the `[Timestamp]` field. |
| `.Emit("Label")` / `.Emit("Label", [fields])` | Materialize the result set. |
| `.EmitCount("Label")` | Count instead of materializing. |
| `.EmitSummary()` / `.EmitNeighborsSummary()` | Aggregate stats — invaluable smoke check. |

Always include 3–5 of these in your connector's documentation so consumers can verify the result themselves.

## Re-running safely (idempotency)

A well-built connector should produce the **same graph** whether it runs once or a hundred times. Three rules:

1. **Use deterministic keys.** Either lift a real ID from the source, or compute a stable hash of the row's identifying fields. Never `Guid.NewGuid()` — it produces a duplicate node every run.

   ```csharp
   // good: deterministic from input
   var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{source}|{externalId}")));

   // bad: non-deterministic
   var id = Guid.NewGuid().ToString();
   ```

2. **Use `TryAdd` for static facts, `AddOrUpdate` for things that change.** A `Customer.Email` rarely changes; their `Name` might. So `TryAdd` the customer the first time, but `AddOrUpdate` if you want re-runs to refresh the display name.

3. **Use `UnlinkExcept` before re-linking** for fields that can change values (status, owner, category). Without it, a case that moved from `Open` → `Closed` will end up linked to **both** `Status` nodes.

## Common pitfalls

- **Forgetting `await graph.CommitPendingAsync()`.** Disposal flushes too, but if the program exits abnormally beforehand, queued operations are lost. Commit explicitly.
- **Random keys.** Re-runs duplicate the graph. Always derive keys from the source.
- **Silent CSV column mismatches.** `CsvHelper` quietly leaves properties at default if header names don't match. Verify by printing the first parsed row before ingestion.
- **One-directional `Link` calls.** Always pass forward + reverse edge names.
- **Linking before the target exists.** If you emit an edge to a node that hasn't been `TryAdd`-ed yet, it dangles. Either order ingestion so targets come first, or use `Node.FromKey(type, key)` — the server resolves it once the node appears.
- **Skipping schema registration.** Call `CreateNodeSchemaAsync<T>()` for every type and `CreateEdgeSchemaAsync(typeof(Edges))` for the edge class **before** the first `TryAdd`/`Link`. Otherwise the server has nothing to validate against.
- **Mutating shared entities from parallel threads.** Wrap shared lookups (manufacturers, statuses, tags) in `ConcurrentDictionary.GetOrAdd`.
- **Hard-coded paths.** Make data paths configurable via environment variables so the connector runs from any working directory.
- **Unbounded queues.** Commit in batches (every 1k–10k rows) for large ingestions.
- **Renaming a `[Key]` mid-life.** From the server's perspective, that's a new node type. Add new types, don't rename keys, or migrate explicitly.

## End-to-end example

A complete connector that reads a CSV of support cases and produces the graph from the schema above.

**`src/Schema.cs`** — as defined in [Modeling nodes and edges](#modeling-nodes-and-edges).

**`src/ingestion/CsvLoader.cs`:**

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;

namespace MyConnector;

public sealed class CaseRow
{
    public string CaseId       { get; set; } = string.Empty;
    public string CustomerEmail{ get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string DeviceSerial { get; set; } = string.Empty;
    public string DeviceModel  { get; set; } = string.Empty;
    public string Summary      { get; set; } = string.Empty;
    public string Body         { get; set; } = string.Empty;
    public string Status       { get; set; } = string.Empty;
    public string Agent        { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
}

public static class CsvLoader
{
    public static IEnumerable<CaseRow> Load(string path)
    {
        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
        foreach (var row in csv.GetRecords<CaseRow>())
            yield return row;
    }
}
```

**`src/Program.cs`:**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using MyConnector;
using static MyConnector.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN")
                    ?? throw new InvalidOperationException("Set CURIOSITY_API_TOKEN.");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "Support CSV";
var inputPath     = Environment.GetEnvironmentVariable("INPUT_CSV")                ?? "data/cases.csv";

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("Connector");

if (!File.Exists(inputPath))
{
    logger.LogError("Input file not found: {Path}", inputPath);
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName)
                       .WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await CreateSchemasAsync(graph);
await IngestAsync(graph, CsvLoader.Load(inputPath), logger);
await graph.CommitPendingAsync();
await PrintCountsAsync(graph);

static async Task CreateSchemasAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<Nodes.Customer>();
    await graph.CreateNodeSchemaAsync<Nodes.Device>();
    await graph.CreateNodeSchemaAsync<Nodes.SupportCase>();
    await graph.CreateNodeSchemaAsync<Nodes.Status>();
    await graph.CreateNodeSchemaAsync<Nodes.Agent>();
    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

static async Task IngestAsync(Graph graph, IEnumerable<CaseRow> rows, ILogger logger)
{
    int total = 0;
    foreach (var row in rows.OrderBy(r => r.OpenedAt))
    {
        var customer = graph.TryAdd(new Nodes.Customer { Email = row.CustomerEmail, Name = row.CustomerName });
        var device   = graph.TryAdd(new Nodes.Device   { SerialNumber = row.DeviceSerial, Model = row.DeviceModel });
        var agent    = graph.TryAdd(new Nodes.Agent    { Username = row.Agent });
        var status   = graph.TryAdd(new Nodes.Status   { Value = row.Status });

        var supportCase = graph.AddOrUpdate(new Nodes.SupportCase
        {
            Id       = row.CaseId,
            Summary  = row.Summary,
            Body     = row.Body,
            OpenedAt = row.OpenedAt
        });

        graph.Link(customer, device,        Edges.Owns,       Edges.OwnedBy);
        graph.Link(supportCase, customer,   Edges.RaisedBy,   Edges.Raised);
        graph.Link(supportCase, device,     Edges.ForDevice,  Edges.HasSupportCase);
        graph.Link(supportCase, agent,      Edges.AssignedTo, Edges.Handles);

        graph.UnlinkExcept(supportCase, status, Edges.HasStatus, Edges.StatusOf);
        graph.Link(supportCase, status, Edges.HasStatus, Edges.StatusOf);

        if (++total % 1000 == 0)
        {
            await graph.CommitPendingAsync();
            logger.LogInformation("Committed {Total} cases", total);
        }
    }
    logger.LogInformation("Ingested {Total} cases", total);
}

static async Task PrintCountsAsync(Graph graph)
{
    foreach (var label in new[]
    {
        nameof(Nodes.Customer), nameof(Nodes.Device),
        nameof(Nodes.SupportCase), nameof(Nodes.Status), nameof(Nodes.Agent)
    })
    {
        var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
        Console.WriteLine($"  {label}: {resp.GetEmittedCount("C")}");
    }
}
```

**Run it:**

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=...your-token...
export INPUT_CSV=data/cases.csv

dotnet run --project MyConnector
```

**Verify in the workspace Shell** (`Manage → Shell`):

```csharp
return Q().EmitNeighborsSummary();
return Q().StartAt(N.Status.Type, "Open").In(N.SupportCase.Type).Take(10).Emit("N");
return Q().StartAt(N.Customer.Type, "alice@example.com").Out(N.Device.Type).Emit("N");
```

That's the whole shape of a connector. Everything else is variations on the source format.

## License

MIT — see `LICENSE`.
