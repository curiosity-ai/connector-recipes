# PostgreSQL / MySQL Sample — Server-Side Cursors + Watermark Sync

A minimal connector that reads from a production-grade SQL server (PostgreSQL or MySQL), pages through a table using **keyset pagination**, and ingests rows into a Curiosity knowledge-graph workspace. Demonstrates the watermark pattern: each run resumes from where the previous one stopped, so re-running the connector pulls only the rows that changed.

This generalizes `SqlSample` (which uses an in-process SQLite file) to the database servers most production users actually have.

## Code shape

```
PostgresSample/
├── PostgresSample.csproj
├── data/
│   ├── seed.sql           ← schema + 10 sample grants
│   └── docker-compose.yml ← one command to bring a Postgres up locally
└── src/
    ├── SqlServerSource.cs ← generic: URL parsing + keyset pagination + watermark
    ├── Schema.cs          ← dataset-specific: nodes + edges
    ├── GrantsIngest.cs    ← dataset-specific: row record + ingestion
    └── Program.cs         ← ~40-line glue: connect → register → stream → commit
```

`SqlServerSource.cs` is the reusable piece — the same code talks to either flavor via the connection-URL scheme.

## The data

10 research-grant rows joined into a single `grants` table (denormalized on purpose; in production you'd page the join itself, but the shape of the ingestion is the same). Each row carries the PI's email, university, research area, funding agency, and an `updated_at` timestamp — the column the watermark sync rides on.

## The graph

| Source columns | Node type | Key |
|---|---|---|
| `id`, `title`, `amount_usd`, … | `Grant` | `id` |
| `pi_email`, `pi_name` | `Faculty` | `pi_email` (shared with CSV/SqlSample) |
| `university` | `University` | `name` (shared with CSV/SqlSample) |
| `research_area` | `ResearchArea` | `name` (shared with SqlSample) |
| `agency_*` | `FundingAgency` | `acronym` |

Edges:

```
Grant ──AwardedTo──> Faculty ──AffiliatedWith──> University
Grant ──Covers──> ResearchArea
Grant ──FundedBy──> FundingAgency
```

## Source abstraction

```csharp
public sealed class SqlServerSource
{
    public static SqlServerSource FromUrl(string url, ILogger? logger);
    public List<T>       Query<T>(string sql, Func<DbDataReader, T> map);
    public IEnumerable<T> StreamPaged<T>(string sqlTemplate, string startKey, Func<DbDataReader, T> map, Func<T, string> keyOf, int pageSize);
    public sealed class Watermark { public string Read(string fallback); public void Write(string value); }
}
```

- `FromUrl` parses `postgres://user:pass@host:port/db` or `mysql://…` and picks the right driver.
- `StreamPaged` runs a `WHERE keyCol > @startKey ORDER BY keyCol LIMIT @pageSize` loop — bounded memory, no OFFSET re-scan, naturally incremental.
- `Watermark` is a one-line JSON-less file at `data/.watermark`; swap for a workspace-side metadata node if you want it co-located with the graph.

## Running

### 1. Bring up a local Postgres (one command)

```bash
cd PostgresSample/data
docker compose up -d
```

The seed script auto-runs on first start.

### 2. Run the connector

```bash
export CURIOSITY_API_TOKEN=<workspace token>
export RECIPE_DB_URL=postgres://recipes:recipes@localhost:5432/grants_db
cd PostgresSample
dotnet run
```

Re-run it — only rows with `updated_at > <watermark>` come through. Touch a row in Postgres and watch the next run pick up only that change:

```sql
UPDATE grants SET status = 'Closed', updated_at = now() WHERE id = 'G-2024-001';
```

### Using MySQL instead

Uncomment the `mysql:` service in `docker-compose.yml`, then:

```bash
export RECIPE_DB_URL=mysql://recipes:recipes@localhost:3306/grants_db
dotnet run
```

The driver auto-switches on URL scheme.

## Sample queries

```csharp
// Every grant for one PI.
return Q().StartAt(N.Faculty.Type, "h.ortega@cmu.edu")
          .Out(N.Grant.Type, E.Holds)
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students advised by PIs holding grants
// in their research area — Student → Advisor (= Faculty) → Grant → ResearchArea.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Faculty.Type,      E.AdvisedBy)
          .Out(N.Grant.Type,        E.Holds)
          .Out(N.ResearchArea.Type, E.Covers)
          .Emit("N");
```

```csharp
// Total dollars per agency.
return Q().StartAt(N.FundingAgency.Type)
          .EmitNeighborsSummary();
```

## The watermark, in more detail

Naive re-runs of a connector duplicate work and risk overwriting newer data with stale property values. Two ways to avoid that:

1. **OFFSET pagination** — easy, but every page rescans `offset + limit` rows on the server. Becomes O(n²) on large tables.
2. **Keyset pagination on an indexed monotonic column** — `updated_at`, `id`, an event sequence number. Constant cost per page, naturally incremental, recoverable mid-run by re-reading the watermark file.

This recipe uses (2) with `updated_at`. The recipe persists the highest seen `updated_at` after each successful commit; an interrupted run picks up from the last fully committed page, never the last fetched row.

Pitfalls of `updated_at`:

- **Clock skew.** Multiple writers behind a load balancer can produce slightly out-of-order timestamps. Use a `(updated_at, id)` composite cursor if you need strict ordering — the SQL becomes `WHERE (updated_at, id) > (@k1, @k2)`.
- **Soft deletes.** A delete only shows up if the row is updated (`is_deleted = true`) rather than removed. Hard deletes need a separate tombstone stream or a CDC source (see the Kafka sample).

## Reusing this recipe

**Keep as-is**
- `SqlServerSource.cs` — both flavors, both pagination modes, and the watermark file are dataset-agnostic.
- `Program.cs` — connect / page / commit loop.
- `PostgresSample.csproj` — `Npgsql`, `MySqlConnector`, `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — your domain's nodes and edges.
- `GrantsIngest.cs`:
  1. The `GrantRow` record — one field per SELECT column.
  2. `PagedSql` — the SELECT … WHERE keyCol > @startKey ORDER BY keyCol LIMIT @pageSize statement.
  3. `Map` — `DbDataReader` → row record.
  4. `Ingest` — emit nodes / link edges per row.

**Tweak in `Program.cs`**
- The watermark column type (date string here; could be a numeric ID).
- The `keyOf` lambda passed to `StreamPaged`.
- The connector display name and default `pageSize`.
