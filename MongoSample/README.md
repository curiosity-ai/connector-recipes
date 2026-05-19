# MongoDB Sample — Schemaless Documents Into Typed Graph

A minimal connector that reads from a MongoDB collection (or a local mirror), projects nested arrays into typed nodes and edges, and ingests them into a Curiosity knowledge-graph workspace. Demonstrates two patterns that are MongoDB-specific:

1. **BSON → JSON → typed POCO** — the relaxed extended-JSON round-trip is the safe way to handle `$oid`, `$date`, `$numberLong`, and other BSON-only types when projecting into C# records.
2. **Change streams** — once the initial snapshot is in, `RECIPE_FOLLOW_CHANGES=1` keeps the graph fresh by listening for inserts, updates, and replaces.

## Code shape

```
MongoSample/
├── MongoSample.csproj
├── data/
│   └── profiles/                  ← one .json file per document (offline mirror)
│       ├── S001.json
│       ├── S002.json
│       └── S003.json
└── src/
    ├── MongoSource.cs    ← generic: IMongoSource + Mongo + Local implementations
    ├── Schema.cs         ← dataset-specific: nodes + edges
    ├── ProfilesIngest.cs ← dataset-specific: document model + ingestion
    └── Program.cs        ← ~50-line glue: snapshot → optional change-stream → commit
```

`MongoSource.cs` is the reusable piece — both the real driver and the local fallback emit the same typed `IAsyncEnumerable<T>`.

## The data

Each document in the `profiles` collection enriches one student with their public-facing portfolio: skills, interests, internships, and projects. Nested arrays are the main pattern this recipe demonstrates — flat columns map onto properties, nested objects fan out into separate nodes.

```jsonc
// profiles/S001.json
{
  "studentId": "S001",
  "bio": "...",
  "skills":    ["Python", "TensorFlow", ...],
  "interests": ["NLP", "Robotics", ...],
  "internships": [{ "company": "OpenAI", "role": "Research Intern", "startYear": 2024, ... }],
  "projects":    [{ "name": "Notebook-LM clone", "url": "...", "skills": [...] }]
}
```

## The graph

| Source field | Node type | Key |
|---|---|---|
| `studentId`, `bio`, `githubUrl`, `linkedin` | `Student` | `studentId` (shared with CSV) |
| `skills[*]` (top-level + per-project) | `Skill` | `name` (shared with CSV/JSON) |
| `interests[*]` | `Interest` | `name` |
| `internships[*]` | `Internship` | `"<student>/<company>/<startYear>"` |
| `internships[*].company` + `industry`, `website` | `Company` | `name` |
| `projects[*]` | `Project` | `"<student>/<projectName>"` |

Edges:

```
Student ──Uses──> Skill
Student ──InterestedIn──> Interest
Student ──Held──> Internship ──At──> Company
Student ──Built──> Project ──Uses──> Skill
```

## Source abstraction

```csharp
public interface IMongoSource
{
    IAsyncEnumerable<T> StreamAsync<T>(string collection, CancellationToken ct = default);
    IAsyncEnumerable<T> StreamChangesAsync<T>(string collection, CancellationToken ct = default);
}
```

- `MongoSource` — `MongoClient.GetCollection<BsonDocument>` + cursor batching for the snapshot, `WatchAsync` with `UpdateLookup` for the change stream.
- `LocalMongoSource` — recursive directory walk over `*.json`. The change-stream method is a no-op for the local source.

The BSON-to-typed step uses `BsonDocument.ToJson` with `JsonOutputMode.RelaxedExtendedJson` then `System.Text.Json` — the simplest way to support BSON-only types without pulling in extra dependencies.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd MongoSample

# Local mode — default, works out of the box:
dotnet run

# Real Mongo — initial snapshot only:
RECIPE_MONGO_URI=mongodb://localhost:27017 \
RECIPE_MONGO_DB=students \
RECIPE_MONGO_COLLECTION=profiles \
dotnet run

# Real Mongo — initial snapshot then follow change stream:
RECIPE_MONGO_URI=mongodb://localhost:27017 \
RECIPE_FOLLOW_CHANGES=1 \
dotnet run
```

Change streams require the server to be a **replica set or sharded cluster** — a standalone `mongod` won't surface change events. The Mongo Atlas free tier ships as a replica set out of the box; for local dev, `docker run mongo:7 --replSet rs0` plus an `rs.initiate()` is enough.

## Sample queries

```csharp
// Where has S001 interned?
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Internship.Type, E.Held)
          .Out(N.Company.Type,    E.At)
          .Emit("N");
```

```csharp
// Combined with the JSON sample: skills that students used in projects AND
// that have a learning resource attached — Student → Skill → LearningResource.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Skill.Type,           E.Uses)
          .Out(N.LearningResource.Type, E.TaughtBy)
          .Emit("N");
```

```csharp
// Companies hiring interns into the Finance industry.
return Q().StartAt(N.Company.Type)
          .Where(c => c.GetString(N.Company.Industry) == "Finance")
          .Out(N.Internship.Type, E.Hosted)
          .Out(N.Student.Type,    E.HeldBy)
          .Emit("N");
```

## Change streams in more detail

The first ingestion run captures the current snapshot of the collection. Subsequent runs with `RECIPE_FOLLOW_CHANGES=1` open a change-stream cursor and apply updates as they happen.

Three operational notes:

- **Resume tokens.** A production connector should persist the last-seen `_data` token after each successful commit and pass it to `WatchAsync` via `StartAfter`. This recipe omits that for clarity — full code lives in [`MongoSource.StreamChangesAsync`](./src/MongoSource.cs); add `ChangeStreamOptions.StartAfter` to make it resumable.
- **Schema drift.** A field added to one document but missing from another deserializes to `default(T)` silently. Validate critical fields in `Ingest` rather than `Map`.
- **`fullDocument: updateLookup`.** Without this option, change events for updates carry only the diff. With it, Mongo re-reads the post-update document so ingestion code stays simple. Storage overhead is negligible for typical workloads.

## Reusing this recipe

**Keep as-is**
- `MongoSource.cs` — both implementations are dataset-agnostic.
- `Program.cs` — snapshot + optional change-stream loop.
- `MongoSample.csproj` — `MongoDB.Driver` + `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — node types and edges for your domain.
- `ProfilesIngest.cs`:
  1. The `ProfileDoc` (and any nested doc classes) — mirror your collection's shape.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — emit nodes (`AddOrUpdate` for the document root, `TryAdd` for de-duped vocabulary nodes like Skills/Companies) and link them.

**Tweak in `Program.cs`**
- The default collection name.
- Whether to follow changes by default.
- Composite key strategies if your top-level documents share fields.
