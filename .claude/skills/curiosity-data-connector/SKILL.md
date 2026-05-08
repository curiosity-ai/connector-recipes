---
name: curiosity-data-connector
description: Use when building or editing a Curiosity data connector — a .NET console app that reads a source format (CSV, JSON, S3, REST, SQL, …) and ingests it into a Curiosity knowledge-graph workspace via the `Curiosity.Library` SDK. TRIGGER when a .cs file uses `using Curiosity.Library;` or `Graph.Connect(...)`; csproj has `PackageReference Include="Curiosity.Library"`; classes are decorated with `[Node]` / `[Key]` / `[Property]` / `[Timestamp]`; code calls `graph.TryAdd`, `graph.Link`, `graph.AddOrUpdate`, `graph.CreateNodeSchemaAsync`, `graph.CommitPendingAsync`, or builds queries via `Q().StartAt(...)` / `q.StartAt(...)`; project name or folder contains "DataConnector" / "data-connector" / "ConnectorRecipe"; user asks about ingesting CSV/JSON/etc into Curiosity, defining graph schemas, edges, or running a connector against a workspace. SKIP for Tesserae frontends (use the tesserae-frontend skill instead), the Mosaik server source itself, scrapers that don't talk to the graph, and unrelated .NET projects.
---

# Building a Curiosity Data Connector

A data connector is a small .NET 10 console app that:

1. Connects to a Curiosity workspace via `Graph.Connect(url, token, name)`.
2. Registers a **schema** (node types + edge type names) on the graph.
3. **Ingests** rows from a source format → emits nodes (`graph.TryAdd`/`AddOrUpdate`) and edges (`graph.Link`).
4. Commits pending operations (`graph.CommitPendingAsync()`).
5. Optionally runs a few `Q()` queries to verify counts / shape.

The reference application is **Mosaik** (`/Users/omar/Soft/Curiosity/mosaik`). The minimal canonical example is `/Users/omar/Soft/Curiosity/technical-support-demo/technical-support/data-connector/`. A richer real-world example is `/Users/omar/Soft/Curiosity/nuclear-knowledge/nuclear-knowledge/data-connector/Asnr/`. Prefer their conventions over inventing new ones.

## Hard constraints

- **Target framework:** `net10.0`. Modern C# is fine (records, nullable, switch expressions, top-level statements).
- **One project = one source format.** A CSV recipe shouldn't also do JSON. If the recipe is *about* combining sources, that's the recipe's topic.
- **Deterministic keys.** Either lift a real ID from the source or compute a stable hash. Random IDs duplicate the graph on re-run.
- **`Curiosity.Library` is the only required NuGet.** Add format-specific packages (`CsvHelper`, `Newtonsoft.Json`, `AWSSDK.S3`, `Npgsql`, …) per recipe.

## 1. Project structure

```
<Source>ConnectorRecipe/
├── <Source>ConnectorRecipe.csproj
├── data/                              # tiny sample input shipped with the recipe
└── src/
    ├── Program.cs                     # entry: connect → register schema → ingest → query
    ├── Schema.cs                      # public static partial class Schema { Nodes; Edges; }
    ├── nodes/<Type>.cs                # one partial-class file per node type (optional but recommended)
    └── ingestion/<Source>Loader.cs    # source-specific reader/parser
```

`Schema.cs` holds an `Edges` static class (edge name string constants) and an empty `Nodes` partial that gets filled in by `nodes/*.cs` partial classes — splitting nodes across files keeps each one focused and makes diffs readable.

### csproj template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>ConnectorRecipes.<Source></RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Curiosity.Library" Version="26.4.469" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.6" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.6" />
    <!-- format-specific: CsvHelper, Newtonsoft.Json, AWSSDK.S3, Npgsql, ... -->
  </ItemGroup>
</Project>
```

## 2. Schema definition

Schemas are pure C# classes with attributes from `Curiosity.Library`. The four attributes are: `[Node]` (class-level — marks the class as a graph node type), `[Key]` (identity property — must be unique), `[Property]` (regular indexed field), `[Timestamp]` (a `DateTimeOffset` used for time-based queries / sorting).

### Single-file pattern (small recipes)

```csharp
using System;
using Curiosity.Library;

namespace ConnectorRecipes.Csv;

public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Device
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class SupportCase
        {
            [Key]       public string Id      { get; set; } = string.Empty;
            [Property]  public string Summary { get; set; } = string.Empty;
            [Property]  public string Content { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset Time { get; set; }
        }
    }

    public static class Edges
    {
        public const string HasSupportCase = nameof(HasSupportCase);
        public const string ForDevice      = nameof(ForDevice);
    }
}
```

### Partial-class pattern (richer recipes)

`Schema.cs` declares the shape and edges; one file per node type populates `Nodes`:

```csharp
// Schema.cs
public static partial class Schema
{
    public static partial class Nodes { }

    public static class Edges
    {
        public const string HasReactor    = nameof(HasReactor);
        public const string BelongsToSite = nameof(BelongsToSite);
    }
}

// nodes/Reactor.cs
public static partial class Schema
{
    public static partial class Nodes
    {
        [Node]
        public class Reactor
        {
            [Key]      public string Id   { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }
    }
}
```

### Conventions

- **Edges come in pairs.** Forward + reverse: `HasSupportCase` / `ForDevice`, `HasManufacturer` / `ManufacturerOf`. Always pass both to `graph.Link(a, b, forward, reverse)` so traversal works in either direction.
- **`nameof(X)` for edge constants.** Cheaper to refactor than string literals.
- **Default property values to `string.Empty`** if `Nullable` is enabled — keeps the graph tidy and avoids null-handling churn.
- **Promote shared categorical fields to nodes.** A `Status` field repeated across rows is more useful as a `Status` node linked by `HasStatus`/`StatusOf` — enables filtering and faceted navigation later.
- **Generate a key when none exists.** Either deterministic (`$"SC-{i:0000}"` from a stable order, or a hash of the source row) or sequential — but sequential only works if the input order is stable across runs.

## 3. Program.cs — the canonical loop

Top-level statements; one program flows: read env → connect → register → ingest → commit → verify.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Curiosity.Library;
using Microsoft.Extensions.Logging;
using ConnectorRecipes.Csv;
using static ConnectorRecipes.Csv.Schema;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "CSV Recipe";
var smoke         = Environment.GetEnvironmentVariable("RECIPE_SMOKE") == "1";
var maxRows       = int.TryParse(Environment.GetEnvironmentVariable("RECIPE_MAX_ROWS"), out var m) ? m : int.MaxValue;

if (!smoke && string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN. Set RECIPE_SMOKE=1 to dry-run without a workspace.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("Connector");

var rows = CsvLoader.Load(Path.Combine("..", "data", "input.csv")).Take(maxRows).ToList();

if (smoke)
{
    foreach (var r in rows.Take(5)) Console.WriteLine($"[smoke] {r}");
    Console.WriteLine($"[smoke] {rows.Count} rows parsed; skipping graph upload.");
    return;
}

using var graph = Graph.Connect(workspaceUrl, apiToken!, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await CreateSchemasAsync(graph);
await IngestAsync(graph, rows, logger);
await graph.CommitPendingAsync();
await PrintCountsAsync(graph);

static async Task CreateSchemasAsync(Graph graph)
{
    await graph.CreateNodeSchemaAsync<Nodes.Device>();
    await graph.CreateNodeSchemaAsync<Nodes.SupportCase>();
    await graph.CreateEdgeSchemaAsync(typeof(Edges));
}

static async Task IngestAsync(Graph graph, List<CsvRow> rows, ILogger logger)
{
    logger.LogInformation("Ingesting {Count} rows", rows.Count);
    int caseId = 0;
    foreach (var row in rows.OrderBy(r => r.Time))
    {
        var caseNode   = graph.TryAdd(new Nodes.SupportCase { Id = $"SC-{caseId++:0000}", Summary = row.Summary, Content = row.Content, Time = row.Time });
        var deviceNode = graph.TryAdd(new Nodes.Device { Name = row.Device });
        graph.Link(caseNode, deviceNode, Edges.ForDevice, Edges.HasSupportCase);
    }
    await Task.CompletedTask;
}

static async Task PrintCountsAsync(Graph graph)
{
    foreach (var label in new[] { nameof(Nodes.Device), nameof(Nodes.SupportCase) })
    {
        var resp = await graph.QueryAsync(q => q.StartAt(label).EmitCount("C"));
        Console.WriteLine($"  {label}: {resp.GetEmittedCount("C")}");
    }
}
```

## 4. Graph API — the moves you'll actually use

All operations go through the `Graph` returned by `Graph.Connect(...)`. The whole API is in `/Users/omar/Soft/Curiosity/mosaik/Library/Curiosity.Library/src/Graph.Public.cs`. The methods that come up in 95% of recipes:

### Adding nodes

```csharp
// Insert if missing, return existing if present (by [Key]). Idempotent.
var deviceNode = graph.TryAdd(new Nodes.Device { Name = "iPhone-12" });

// Insert OR update properties on an existing node. Use when re-running
// should refresh property values.
var caseNode = graph.AddOrUpdate(new Nodes.SupportCase { Id = "SC-0001", Summary = "..." });

// Reference an existing node by (type, key) without re-creating it —
// useful when linking to a node you don't have a handle for.
graph.Link(partNode, Node.FromKey(nameof(Nodes.Device), "iPhone-12"), Edges.PartOf, Edges.HasPart);
```

### Adding edges

```csharp
// Always pass forward + reverse names for bidirectional traversal.
graph.Link(caseNode, deviceNode, Edges.ForDevice, Edges.HasSupportCase);

// Replace edges of a given type before re-linking — useful for fields
// that can change (status, category, owner) so the old edge doesn't linger.
graph.UnlinkExcept(caseNode, statusNode, Edges.HasStatus, Edges.StatusOf);
graph.Link(caseNode, statusNode, Edges.HasStatus, Edges.StatusOf);
```

### Aliases (for search / NLP)

```csharp
// Make a node also findable by alternate names. Mosaik.Core.Language.Any
// applies the alias in every language.
graph.AddAlias(deviceNode, Mosaik.Core.Language.Any, "iPhone 12", ignoreCase: false);
graph.AddAlias(deviceNode, Mosaik.Core.Language.Any, "iPhone.12", ignoreCase: false);
```

### Committing

```csharp
// Flush queued operations to the server. Always call before the program
// exits — the using/Dispose path will also flush, but explicit is better.
await graph.CommitPendingAsync();
```

For batched ingestion (lots of rows), commit periodically inside the loop to avoid building an unbounded in-memory queue:

```csharp
int batch = 0;
foreach (var row in rows)
{
    /* … emit nodes/edges … */
    if (++batch % 1000 == 0) await graph.CommitPendingAsync();
}
await graph.CommitPendingAsync();
```

### Parallel ingestion

`Parallel.ForEachAsync` is fine — `TryAdd`/`Link` are thread-safe — but de-dupe shared keys yourself with a `ConcurrentDictionary` so you don't issue redundant `AddOrUpdate` calls for the same shared entity (e.g. a `Manufacturer` referenced by 10k parts).

```csharp
var manufacturers = new ConcurrentDictionary<string, Node>();
await Parallel.ForEachAsync(rows, async (row, _) =>
{
    var mfg = manufacturers.GetOrAdd(row.Manufacturer, m => graph.TryAdd(new Nodes.Manufacturer { Name = m }));
    var part = graph.TryAdd(new Nodes.Part { Name = row.PartName });
    graph.Link(part, mfg, Edges.HasManufacturer, Edges.ManufacturerOf);
});
```

## 5. The `Q()` query language (verification & exploration)

Two ways to run queries:

**From the connector** (top-level `Q()` is not in scope; use `graph.QueryAsync(q => …)`):

```csharp
var resp = await graph.QueryAsync(q => q.StartAt(nameof(Nodes.Device)).EmitCount("C"));
var count = resp.GetEmittedCount("C");

var resp2 = await graph.QueryAsync(q => q.StartAt(nameof(Nodes.Device)).Take(10).Emit("N", [nameof(Nodes.Device.Name)]));
var devices = resp2.GetEmitted("N").ToDictionary(n => n.UID, n => n.GetField<string>(nameof(Nodes.Device.Name)));
```

**From the Curiosity Shell** (admin sidebar → `Shell` — paste the snippet, hit run):

```csharp
return Q().StartAt(N.Device.Type).Take(10).Emit("N");

// follow a relationship
return Q().StartAt(N.Manufacturer.Type, "Apple").Out(N.Part.Type).Emit("N");

// timestamped — newest first
return Q().StartAt(N.SupportCase.Type).SortByTimestamp(oldestFirst: false).Take(10).Emit("N");

// neighbor-summary for one node type — best smoke test that ingestion worked
return Q().StartAt(N.Part.Type).EmitNeighborsSummary();

// whole-graph relationship summary
return Q().EmitNeighborsSummary();

// filtering with .Where(...)
return Q().StartAt(N.Device.Type)
          .Where(n => n.GetString(N.Device.Name).Contains("iPhone"))
          .Emit("N");

// add edges in a transaction
foreach (var manufacturerNode in Q().StartAt(N.Manufacturer.Type).AsEnumerable())
{
    var name = manufacturerNode.GetString(N.Manufacturer.Name);
    await Q().StartAt(N.Device.Type)
             .Where(n => n.GetString(N.Device.Name).Contains(name))
             .Tx()
             .AddUniqueEdgeTo(E.HasManufacturer, manufacturerNode.UID)
             .AddUniqueEdgeFrom(E.ManufacturerOf, manufacturerNode.UID)
             .CommitAsync();
}
```

**Common chain segments** (full reference: `/Users/omar/Soft/Curiosity/mosaik/Mosaik/Graph/src/Queries/`):

| Method | Purpose |
|---|---|
| `.StartAt(type)` / `.StartAt(type, key)` / `.StartAt(uid)` | Anchor traversal at a node type, a typed key, or a specific UID. |
| `.Out(type, edgeType?)` / `.In(type, edgeType?)` | Walk forward / reverse along an edge. |
| `.Where(n => …)` | Predicate filter on properties (use `n.GetString("Field")`, `n.GetInt(...)`). |
| `.Take(n)` / `.Skip(n)` | Pagination. |
| `.SortByTimestamp(oldestFirst)` | Order time-stamped nodes. |
| `.Emit("N")` / `.Emit("N", [field, …])` | Materialize results under a label, optionally projecting fields. |
| `.EmitCount("C")` | Just count — cheap. |
| `.EmitSummary()` / `.EmitNeighborsSummary()` | High-level stats — invaluable smoke check. |
| `.AsEnumerable()` | Pull results client-side for arbitrary `Linq` (Shell only). |
| `.Tx().AddUniqueEdgeTo(...).CommitAsync()` | Bulk edge creation in one transaction. |

Always include 3–5 sample queries in the recipe's README so users can verify the resulting graph in the Shell.

## 6. Environment-variable conventions

Every recipe should accept the same baseline plus its own source-specific overrides. Keep names UPPER_SNAKE_CASE.

| Variable | Default | Purpose |
|---|---|---|
| `CURIOSITY_URL` | `http://localhost:8080/` | Workspace URL |
| `CURIOSITY_API_TOKEN` | — | **Required**. Generate in the workspace under `Manage → API integrations → Create API Token`. |
| `CURIOSITY_CONNECTOR_NAME` | per-recipe default | Connector identity in the Curiosity UI. |
| `<RECIPE>_SMOKE` | unset | If `1`, parse input only and skip graph commits — use this to iterate on parsers without re-uploading. |
| `<RECIPE>_MAX_ROWS` / `_MAX_FILES` | `int.MaxValue` | Cap input size for fast iteration against a real workspace. |
| Source-specific paths/credentials | auto-detected via `FindFirstExisting(...)` | E.g. `RECIPE_CSV_PATH`, `RECIPE_S3_BUCKET`, `RECIPE_DB_CONN`. |

The `FindFirstExisting(params string[] candidates)` helper from `AsnrDataConnector/src/Program.cs` is worth copying — it makes the recipe runnable from several working directories (repo root, project dir, `bin/`).

## 7. Smoke-test mode (mandatory for non-trivial parsers)

If the recipe does any meaningful parsing (regex over PDFs, structured extraction from messy CSVs, JSON unwrapping), expose a `<RECIPE>_SMOKE=1` env var that runs the parser and prints extracted fields **without** touching the graph. This lets you iterate on extraction logic without burning workspace state.

Pattern (see `AsnrDataConnector/src/Program.cs:87-129` for the full version):

```csharp
if (smoke)
{
    foreach (var row in rows.Take(maxRows))
    {
        var parsed = MyParser.Parse(row);
        Console.WriteLine($"--- {row.Id}");
        Console.WriteLine($"  Field A: {parsed.A}");
        Console.WriteLine($"  Field B: {parsed.B}");
    }
    return;
}
```

## 8. Source-format quick-starts

### CSV (`CsvHelper`)

```csharp
using var reader = new StreamReader(path);
using var csv    = new CsvReader(reader, CultureInfo.InvariantCulture);
foreach (var row in csv.GetRecords<MyRow>()) yield return row;
```

Map columns explicitly with `[Name("ColumnHeader")]` if the header doesn't match the C# property — silent mismatches are the #1 cause of empty graphs.

### JSON (`Newtonsoft.Json` or `System.Text.Json`)

```csharp
var items = JsonConvert.DeserializeObject<MyJson[]>(File.ReadAllText(path));
```

### S3 (`AWSSDK.S3`)

```csharp
var s3   = new AmazonS3Client(/* creds from env */);
var list = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix });
foreach (var obj in list.S3Objects)
{
    using var resp   = await s3.GetObjectAsync(bucket, obj.Key);
    using var stream = resp.ResponseStream;
    /* parse stream → emit nodes/edges */
}
```

Read in streaming fashion — never `await s3.GetObjectAsync(...).ReadToEndAsync()` for large objects in a loop.

### REST API

Use `HttpClient` (single static instance) + `System.Text.Json`. Honor `Retry-After` and back off on `429`.

### SQL (`Npgsql` / `Microsoft.Data.SqlClient`)

`SELECT` only — read in batches with `LIMIT … OFFSET …` or a keyset-paged cursor. Don't fetch the whole table into memory.

## 9. Common pitfalls

- **Forgetting `await graph.CommitPendingAsync()`.** The `using` will dispose-and-flush, but if the program exits abnormally beforehand, queued operations are lost. Always commit explicitly.
- **Random / non-deterministic keys.** Re-running the connector duplicates the graph. Generate keys from stable hashes of the source row, not `Guid.NewGuid()`.
- **Silent column-mismatches in CSV.** `CsvHelper` quietly leaves properties at default if header names don't match. Add `[Name("…")]` or use a `ClassMap`. Verify in smoke mode.
- **Over-fetching.** A connector that loads the entire source into a `List<>` before emitting will OOM on large inputs. Stream rows: `IEnumerable<T>` from the loader, foreach in `IngestAsync`.
- **One-directional `graph.Link`.** Always supply both forward + reverse edge names. A graph linked only forward looks fine in spot-checks but breaks every reverse traversal.
- **Linking to a node that hasn't been created yet.** If you emit edges before the target's `TryAdd`, the edge dangles. Either ensure ingestion order, or use `Node.FromKey(type, key)` to reference by key (the server resolves it once the node exists).
- **Mutating shared entities from parallel threads without dedup.** Two threads calling `graph.TryAdd(new Manufacturer { Name = "Apple" })` simultaneously produces duplicate ops. Wrap shared lookups in `ConcurrentDictionary.GetOrAdd`.
- **Hard-coded paths.** `Path.Combine("..", "data", "x.csv")` works locally but breaks when the recipe is run from a different cwd. Use `FindFirstExisting(...)` with multiple candidates, or an env-var override.
- **Skipping the schema-registration step.** `graph.CreateNodeSchemaAsync<T>()` must run before the first `TryAdd<T>`, otherwise the server has no schema to validate against. `CreateEdgeSchemaAsync(typeof(Edges))` must run before the first `Link` for a given edge name.
- **Hot-reloading the schema mid-run.** Once registered, schemas are sticky. Adding a `[Property]` to an existing `[Node]` class works; renaming a `[Key]` doesn't (it's a new schema). When in doubt, delete the workspace's existing nodes for that type first.
- **Calling the connector against a read-only replica.** Writes silently fail. Verify the workspace URL points to the primary.

## 10. Reference

- **Canonical minimal recipe (read first):** `/Users/omar/Soft/Curiosity/technical-support-demo/technical-support/data-connector/`
  - `INSTRUCTIONS.md` — the walkthrough, including sample queries.
  - `src/Schema.cs` — single-file schema pattern.
  - `src/App.cs` — top-level program with parallel-friendly ingestion.
- **Real-world recipe (more advanced):** `/Users/omar/Soft/Curiosity/nuclear-knowledge/nuclear-knowledge/data-connector/Asnr/`
  - `src/Schema.cs` + `src/nodes/*.cs` — partial-class node split.
  - `src/Program.cs` — `FindFirstExisting`, smoke mode, geo-only mode, `PrintCountsAsync`.
  - `src/ingestion/` — CSV loader, PDF extraction, parser layout.
- **`Curiosity.Library` source (the SDK you call):** `/Users/omar/Soft/Curiosity/mosaik/Library/Curiosity.Library/src/`
  - `Graph.Public.cs` — `Graph.Connect`, `TryAdd`, `Link`, `AddAlias`, `CommitPendingAsync`, `CreateNodeSchemaAsync`.
  - `Attributes/` — `[Node]`, `[Key]`, `[Property]`, `[Timestamp]` definitions.
- **Query language source:** `/Users/omar/Soft/Curiosity/mosaik/Mosaik/Graph/src/Queries/`
  - `IQuery.cs`, `Query.cs`, `QueryInit.cs` — chainable methods on the query builder.
  - `TransactionQuery.cs` — `.Tx().AddUniqueEdgeTo(...).CommitAsync()`.
  - `QueryEnumerableExtensions.cs` — `.AsEnumerable()` and friends for Shell-side `Linq`.

When unsure how to do something: **grep the reference recipes first**, then `Curiosity.Library/src/`. Almost every pattern you need has already been used.
