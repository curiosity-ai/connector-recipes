# REST API Sample — Paginated SaaS Endpoint With Token Auth

A minimal connector that walks a cursor-paginated REST endpoint, deserializes JSON pages, and ingests them into a Curiosity knowledge-graph workspace. Handles bearer-token auth, `429` rate limits with `Retry-After`, and transient `5xx` errors with exponential backoff. The same code path works against a real REST API or against local JSON files for offline development.

## Code shape

```
RestApiSample/
├── RestApiSample.csproj
├── data/
│   └── courses/page-1.json, page-2.json, page-3.json  ← simulated REST responses
└── src/
    ├── RestApiSource.cs    ← generic: IRestApiSource + Http + Local implementations
    ├── Schema.cs           ← dataset-specific: nodes + edges
    ├── CoursesIngest.cs    ← dataset-specific: document model + ingestion
    └── Program.cs          ← ~30-line glue: pick source → register → stream → commit
```

`RestApiSource.cs` is the reusable piece. `Schema.cs` and `CoursesIngest.cs` are what you rewrite for your own REST endpoint.

## The data layout

Every page returned by the endpoint follows the same shape:

```jsonc
{
  "items": [ ... ],
  "nextCursor": "<opaque-cursor>" | null
}
```

When `nextCursor` is null, paging stops. Each item is a course document:

```jsonc
{
  "code": "CS344",
  "title": "Introduction to Machine Learning",
  "level": "Undergraduate",
  "credits": 4,
  "capacity": 180,
  "subjects": ["Machine Learning", "ML Foundations"],
  "terms": ["Spring 2025"],
  "instructors": [
    { "email": "h.ortega@cmu.edu", "name": "Hugo Ortega" }
  ]
}
```

## The graph

| Source field | Node type | Key |
|---|---|---|
| `code`, `title`, `level`, `credits`, `capacity` | `Course` | `code` |
| `subjects[*]` | `Subject` | `name` (shared with CSV/S3) |
| `instructors[*]` | `Faculty` | `email` (shared with CSV's Advisor + SQL's Faculty) |
| `terms[*]` | `Term` | `name` |

Edges:

```
Course ──CoversSubject──> Subject
Course ──TaughtBy──> Faculty
Course ──OfferedIn──> Term
```

## Source abstraction

```csharp
public interface IRestApiSource
{
    IAsyncEnumerable<T> StreamAsync<T>(string path, CancellationToken ct = default);
}
```

- `HttpRestApiSource` — `HttpClient` with bearer auth, cursor-based pagination, `429`/`5xx` retry with exponential backoff (honors `Retry-After` headers).
- `LocalRestApiSource` — reads `page-1.json`, `page-2.json`, … from a local folder using each file's `nextCursor` field as the next filename.

`Program.cs` picks one based on whether `RECIPE_API_BASE_URL` is set. Ingestion code is identical in both modes.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd RestApiSample

# Local mode — default, works out of the box:
dotnet run

# Real REST mode — point at any cursor-paginated endpoint returning the
# { items, nextCursor } envelope:
RECIPE_API_BASE_URL=https://api.example.com/v1 \
RECIPE_API_TOKEN=<bearer> \
dotnet run
```

## Sample queries

```csharp
// Every course covering Machine Learning.
return Q().StartAt(N.Subject.Type, "Machine Learning")
          .Out(N.Course.Type, E.CoveredByCourse)
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students whose enrolled subjects appear
// in the course catalog — Student → Subject → Course.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Subject.Type, E.Studies)
          .Out(N.Course.Type,  E.CoveredByCourse)
          .Emit("N");
```

```csharp
// Faculty (merged with CSV advisors / SQL faculty) teaching this spring.
return Q().StartAt(N.Term.Type, "Spring 2025")
          .Out(N.Course.Type,  E.OffersCourse)
          .Out(N.Faculty.Type, E.TaughtBy)
          .Emit("N");
```

## Adapting the source contract

If your endpoint uses **offset paging**, swap `cursor` for `offset` and bump it by `items.Count` per page. If it uses **link headers** (`Link: <…>; rel="next"`), parse the header into `NextCursor` after each request. If it uses **page numbers**, keep `nextCursor` as a string and serialize the next number into it. The downstream `StreamAsync` contract doesn't change.

## Rate limits and retries

`HttpRestApiSource.GetWithRetryAsync` retries on:

- `HTTP 429` — honors `Retry-After: <seconds>` and `Retry-After: <HTTP-date>` when present, else exponential backoff.
- `HTTP 5xx` — exponential backoff (capped at 60s).
- `HttpRequestException` (network errors) — exponential backoff.

`maxAttempts` is configurable per source instance; the default is 5.

## Reusing this recipe

**Keep as-is**
- `RestApiSource.cs` — both `Http` and `Local` implementations are dataset-agnostic.
- `Program.cs` — http-vs-local selection + glue.
- `RestApiSample.csproj` — Curiosity.Library + logging only (no extra deps).

**Replace for your dataset**
- `Schema.cs` — node types and edges for your domain.
- `CoursesIngest.cs` (rename freely):
  1. The `CourseDoc` class — mirror the JSON shape of one item in the `items` array.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — emit nodes (`TryAdd` / `AddOrUpdate`) and edges (`Link`) per item.

**Tweak in `Program.cs`**
- The endpoint path passed to `StreamAsync` (`"courses"` → your collection).
- Connector display name.
- The type parameter on `StreamAsync<T>` (your renamed document class).
