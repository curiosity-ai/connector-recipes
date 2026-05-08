# S3 Connector Recipe — Subjects, Topics & Books

Companion to [`CsvConnectorRecipe`](../CsvConnectorRecipe/) and [`SkillsJsonConnectorRecipe`](../SkillsJsonConnectorRecipe/). The CSV recipe creates `Subject` nodes (just keys, attached to students). **This recipe deepens those same `Subject` nodes** with topics, recommended books, and book authors.

## Why S3 for subjects

Course materials are document-shaped, plentiful, and live in object storage in the real world: each subject's syllabus is its own file, books have their own metadata files, and the natural access pattern is **walk a prefix and read each object**. That's exactly what S3 gives you. The recipe demonstrates the prefix-walk + per-object-read pattern; reading from a local folder is a drop-in fallback when you don't have AWS credentials handy.

## The data layout

```
bucket/                          (or  data/  locally)
├── subjects/
│   ├── calculus.json
│   ├── linear-algebra.json
│   ├── ml-foundations.json
│   ├── algorithms.json
│   ├── probability.json
│   ├── deep-learning.json
│   ├── operating-systems.json
│   └── statistics.json
└── books/
    ├── 9781285740621.json
    ├── 9780980232776.json
    └── … (one file per ISBN)
```

A subject document references books by **ISBN**, decoupling subject metadata from book metadata so the same book can be referenced from multiple subjects without duplication.

```jsonc
// subjects/deep-learning.json
{
  "name": "Deep Learning",
  "level": "Graduate",
  "description": "Neural networks at scale: CNNs, RNNs, Transformers, and modern training methods.",
  "topics": ["Backpropagation", "CNNs", "RNNs", "Transformers", "Optimization"],
  "bookIsbns": ["9780262035613"]
}

// books/9780262035613.json
{
  "isbn":    "9780262035613",
  "title":   "Deep Learning",
  "year":    2016,
  "edition": 1,
  "authors": ["Ian Goodfellow", "Yoshua Bengio", "Aaron Courville"]
}
```

## Graph additions

| Source | Node type | Key |
|---|---|---|
| `subjects/*.json` (extends CSV's Subject with level + description) | `Subject` | `name` |
| `topics[*]` | `Topic` | `name` |
| `books/*.json` | `Book` | `isbn` |
| `authors[*]` | `Author` | `name` |

Edges:

```
Subject ──Covers──> Topic
Subject ──RecommendsBook──> Book ──WrittenBy──> Author
```

## Source abstraction (S3 ↔ local)

The loader hides the storage backend behind one interface:

```csharp
public interface ISubjectsSource
{
    IAsyncEnumerable<(string Key, string Json)> ListAsync(string prefix);
}
```

- `S3SubjectsSource` — paged `ListObjectsV2Async` + per-object `GetObjectAsync`.
- `LocalSubjectsSource` — recursive directory enumeration + `File.ReadAllTextAsync`.

`Program.cs` picks one based on whether `RECIPE_S3_BUCKET` is set. Ingestion code below it is identical either way.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd SubjectsS3ConnectorRecipe

# Local mode (default, works out of the box):
dotnet run

# Real S3 mode — credentials picked up from the standard AWS chain
# (env vars, ~/.aws/credentials, IAM role, etc.):
RECIPE_S3_BUCKET=my-curriculum-bucket RECIPE_S3_REGION=us-east-1 dotnet run

# Parser dry-run:
RECIPE_SMOKE=1 dotnet run
```

To upload the sample data to your own bucket:

```bash
aws s3 sync data/ s3://my-curriculum-bucket/
```

## Sample queries (paste into the workspace **Shell**)

> Traversal is always `.Out(targetType, edgeName)` — there is no `.In(...)`. To go in the "reverse" direction, pass the paired reverse-edge constant (`RecommendedFor`, `Wrote`, `CoveredIn`, …).

```csharp
// Sanity check — relationship summary for the subject domain.
return Q().StartAt(N.Subject.Type).EmitNeighborsSummary();
```

```csharp
// All topics covered by Calculus.
return Q().StartAt(N.Subject.Type, "Calculus")
          .Out(N.Topic.Type, E.Covers)
          .Emit("N");
```

```csharp
// All books recommended for Deep Learning.
return Q().StartAt(N.Subject.Type, "Deep Learning")
          .Out(N.Book.Type, E.RecommendsBook)
          .Emit("N");
```

```csharp
// Authors of every book recommended for Deep Learning (two-hop).
return Q().StartAt(N.Subject.Type, "Deep Learning")
          .Out(N.Book.Type, E.RecommendsBook)
          .Out(N.Author.Type, E.WrittenBy)
          .Emit("N");
```

```csharp
// Per-book relationship summary — useful for spotting books that span multiple subjects.
return Q().StartAt(N.Book.Type)
          .EmitNeighborsSummary();
```

```csharp
// Combined with the CSV recipe: which students study subjects whose reading
// list includes books by Sheldon Ross? Walk Author → Book → Subject → Student
// using only reverse-edge constants.
return Q().StartAt(N.Author.Type, "Sheldon Ross")
          .Out(N.Book.Type,    E.Wrote)
          .Out(N.Subject.Type, E.RecommendedFor)
          .Out(N.Student.Type, E.StudiedBy)
          .Emit("N");
```

## Files

```
SubjectsS3ConnectorRecipe/
├── SubjectsS3ConnectorRecipe.csproj    # AWSSDK.S3 + Curiosity.Library
├── data/
│   ├── subjects/*.json                 # 8 subjects
│   └── books/*.json                    # 10 books, ISBN-keyed
└── src/
    ├── Schema.cs
    ├── SubjectsS3Loader.cs             # ISubjectsSource + Local & S3 implementations
    └── Program.cs                      # source selection → ingestion → counts
```
