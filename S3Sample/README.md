# S3 Sample — Document Per Object, With a Local Fallback

A minimal connector that walks a prefix of objects (one JSON document per object), deserializes each one, and ingests them into a Curiosity knowledge-graph workspace. The same code path works against an S3 bucket or against a local directory — handy for offline development.

## Code shape

```
S3Sample/
├── S3Sample.csproj
├── data/
│   ├── subjects/*.json
│   └── books/*.json
└── src/
    ├── ObjectStore.cs      ← generic: IObjectStore + Local + S3 implementations
    ├── Schema.cs           ← dataset-specific: nodes + edges
    ├── SubjectsIngest.cs   ← dataset-specific: document models + ingestion
    └── Program.cs          ← ~25-line glue: pick store → register → ingest → commit
```

`ObjectStore.cs` is the reusable piece. `Schema.cs` and `SubjectsIngest.cs` are what you'd rewrite to adapt the recipe to your own document layout.

## The data layout

```
bucket/                          (or  data/  locally)
├── subjects/
│   ├── calculus.json
│   ├── deep-learning.json
│   └── … (one file per subject)
└── books/
    ├── 9780262035613.json
    └── … (one file per ISBN)
```

A subject document references books by **ISBN**, decoupling subject metadata from book metadata so the same book can be referenced from multiple subjects.

```jsonc
// subjects/deep-learning.json
{ "name":"Deep Learning", "level":"Graduate",
  "topics":["Backpropagation","CNNs","RNNs","Transformers"],
  "bookIsbns":["9780262035613"] }

// books/9780262035613.json
{ "isbn":"9780262035613", "title":"Deep Learning", "year":2016, "edition":1,
  "authors":["Ian Goodfellow","Yoshua Bengio","Aaron Courville"] }
```

## The graph

| Source | Node type | Key |
|---|---|---|
| `subjects/*.json` | `Subject` | `name` |
| `topics[*]` | `Topic` | `name` |
| `books/*.json` | `Book` | `isbn` |
| `authors[*]` | `Author` | `name` |

Edges:

```
Subject ──Covers──> Topic
Subject ──RecommendsBook──> Book ──WrittenBy──> Author
```

## Source abstraction

```csharp
public interface IObjectStore
{
    IAsyncEnumerable<(string Key, string Content)> ListAsync(string prefix);
}
```

- `LocalObjectStore` — recursive directory enumeration + `File.ReadAllTextAsync`.
- `S3ObjectStore` — paged `ListObjectsV2Async` + per-object `GetObjectAsync`.

`Program.cs` picks one based on whether `RECIPE_S3_BUCKET` is set. Ingestion code is identical in both modes.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd S3Sample

# Local mode — default, works out of the box:
dotnet run

# Real S3 mode — credentials picked up from the standard AWS chain:
RECIPE_S3_BUCKET=my-bucket RECIPE_S3_REGION=us-east-1 dotnet run
```

Upload the sample data to your own bucket with:

```bash
aws s3 sync data/ s3://my-bucket/
```

## Sample queries

```csharp
// Topics covered by Calculus.
return Q().StartAt(N.Subject.Type, "Calculus")
          .Out(N.Topic.Type, E.Covers)
          .Emit("N");
```

```csharp
// Authors of every book recommended for Deep Learning.
return Q().StartAt(N.Subject.Type, "Deep Learning")
          .Out(N.Book.Type,   E.RecommendsBook)
          .Out(N.Author.Type, E.WrittenBy)
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students whose reading lists include
// books by Sheldon Ross — Author → Book → Subject → Student.
return Q().StartAt(N.Author.Type, "Sheldon Ross")
          .Out(N.Book.Type,    E.Wrote)
          .Out(N.Subject.Type, E.RecommendedFor)
          .Out(N.Student.Type, E.StudiedBy)
          .Emit("N");
```

## Reusing this recipe

**Keep as-is**
- `ObjectStore.cs` — both implementations are dataset-agnostic.
- `Program.cs` — local-vs-S3 selection + glue.
- `S3Sample.csproj` — AWSSDK.S3 + Curiosity.Library.

**Replace for your dataset**
- `Schema.cs` — node types + edges for your domain.
- `SubjectsIngest.cs` (rename freely):
  1. The `*Doc` classes — one per object shape in your bucket (one per prefix is typical).
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `IngestAsync` — read each prefix with `store.ListAsync("yourPrefix/")`, deserialize, emit nodes and edges. If documents cross-reference each other by key (like subjects → books here), load both prefixes into memory first and join on the shared key.

**Tweak in `Program.cs`**
- Connector display name.
- The prefixes you care about (passed to `IngestAsync` via your renamed type).
- The default `localRoot` if your data folder is elsewhere.
