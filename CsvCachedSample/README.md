# CSV Sample (Cached) — Skipping Already-Ingested Rows With HashCache

A near-twin of [`CsvSample`](../CsvSample/) that adds a `HashCache` between the CSV reader and the graph. On the first run every row is ingested; on every subsequent run, unchanged rows are skipped entirely — no graph writes, no network round-trips.

Use this recipe as the starting point whenever:

- You re-ingest the same dump on a schedule (daily CSV drop, hourly export).
- The source has no reliable "modified since" cursor.
- Most rows don't actually change between runs.

For the underlying mechanism see [Caching](https://docs.curiosity.ai/data-connector/caching.html) in the documentation.

## Code shape

```
CsvCachedSample/
├── CsvCachedSample.csproj
├── data/students.csv
└── src/
    ├── CsvSource.cs        ← generic: reads any CSV into typed rows (unchanged from CsvSample)
    ├── Schema.cs           ← dataset-specific: nodes + edges (unchanged from CsvSample)
    ├── StudentsIngest.cs   ← dataset-specific: row model + ingestion + ConnectorVersion
    └── Program.cs          ← load → register → cache-aware loop → commit-then-cache-commit
```

The only file that meaningfully differs from `CsvSample` is `Program.cs`, plus a `ConnectorVersion` constant on `StudentsIngest`.

## What HashCache does

```csharp
using var cache = HashCache.Initialize("cache/csv-students.db");

foreach (var row in rows)
{
    var hash = cache.Hash(row);
    if (cache.ContainsHash(ConnectorVersion, hash)) continue;   // unchanged → skip

    StudentsIngest.Ingest(graph, row);
    cache.EnqueueHash(ConnectorVersion, hash);                  // mark as done (in-memory)
}

await graph.CommitPendingAsync();         // persist graph first
await cache.CommitPendingHashesAsync();   // then persist cache
```

`HashCache` serializes each row to compact JSON, hashes it to a 128-bit value, and stores the hash in a small LiteDB file. Anything you can JSON-serialize works — the row POCO, an anonymous projection, or whatever subset of fields drive ingestion.

The `ConnectorVersion` integer (declared on `StudentsIngest`) is the cache-busting lever: bump it whenever the mapper shape changes (new property, new edge, schema migration) and all cached rows are re-ingested on the next run.

## Commit ordering

The two commits below MUST happen in this order, on this path only:

```csharp
await graph.CommitPendingAsync();         // graph first
await cache.CommitPendingHashesAsync();   // cache after success
```

If `CommitPendingAsync` throws, **do not** call `CommitPendingHashesAsync`. The in-memory pending hashes are discarded when the `cache` instance is disposed; the next run will see those rows as cache misses and retry.

## Running

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=<token from "Manage → API integrations">

cd CsvCachedSample
dotnet run
```

First run output:

```
HashCache: 0 hits, 10 misses
```

Second run, with no changes to `students.csv`:

```
HashCache: 10 hits, 0 misses
```

Edit one row, run again:

```
HashCache: 9 hits, 1 misses
```

### Environment variables

| Name                    | Default                                  | Purpose                              |
| ---                     | ---                                      | ---                                  |
| `CURIOSITY_URL`         | `http://localhost:8080/`                 | Workspace base URL.                  |
| `CURIOSITY_API_TOKEN`   | *(required)*                             | API token from the workspace.        |
| `CURIOSITY_CONNECTOR_NAME` | `CSV Sample (Students, cached)`       | Display name in the workspace.       |
| `RECIPE_CSV_PATH`       | `<bin>/data/students.csv`                | Override the source file.            |
| `RECIPE_CACHE_PATH`     | `<bin>/cache/csv-students.db`            | Override the on-disk cache location. |

The cache file is created on first run. Delete it for a full re-ingest, or bump `StudentsIngest.ConnectorVersion` to invalidate every entry without touching the file.

## Where to put the cache file

`HashCache` is local state, not source data:

- **CI:** cache it (GitHub Actions `actions/cache`, GitLab cache). Misses on a cold runner are correct, not buggy.
- **Docker:** mount a volume at `/var/lib/curiosity-connector/`.
- **Local dev:** the default `<bin>/cache/...` is fine; it's `.gitignore`d.

The CSV ingestion is fully idempotent (`graph.AddOrUpdate` on stable `[Key]`s), so losing the cache only costs you time — the resulting graph is identical.

## Reusing this recipe

The same advice as [`CsvSample`](../CsvSample/) applies: keep `CsvSource.cs` and `Program.cs` glue as-is, rewrite `Schema.cs` and `StudentsIngest.cs` for your dataset. Two extra things to remember:

1. **Pick a `ConnectorVersion` value.** Start at `1`. Bump it whenever `Ingest(...)` starts writing new properties / edges / keys.
2. **Pick what to hash.** Hashing the full `Row` is the simplest choice and the right default. Hash a projection (`new { row.Id, row.Field1, row.Field2 }`) if some columns are volatile but irrelevant to the graph.

## See also

- [`CsvSample`](../CsvSample/) — the uncached baseline this recipe extends.
- [Caching documentation](https://docs.curiosity.ai/data-connector/caching.html) — when to use `HashCache` and how it interacts with idempotency.
- [Idempotency documentation](https://docs.curiosity.ai/data-connector/idempotency.html) — the foundation that makes skipping safe.
