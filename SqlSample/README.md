# SQL Sample — Joining Relational Tables Into a Graph

A minimal connector that reads a SQLite database, joins universities → departments → programs → faculty → research areas, and ingests the result into a Curiosity knowledge-graph workspace. SQLite is used for portability; swapping in PostgreSQL or SQL Server is a one-line change.

## Code shape

```
SqlSample/
├── SqlSample.csproj
├── data/
│   ├── seed.sql                # schema + sample data
│   └── universities.db         # generated on first run (gitignored)
└── src/
    ├── SqliteSource.cs         ← generic: open, EnsureSeeded, Query<T>
    ├── Schema.cs               ← dataset-specific: nodes + edges
    ├── UniversitiesIngest.cs   ← dataset-specific: row records + SQL + ingestion
    └── Program.cs              ← ~25-line glue: seed → register → ingest → commit
```

`SqliteSource.cs` is the reusable piece. `Schema.cs` and `UniversitiesIngest.cs` are what you'd rewrite to adapt the recipe to your own database.

## The data

[`data/seed.sql`](./data/seed.sql) — six tables seeded on first run:

```
universities (name PK, country, founded_year, ranking, students_count, website)
departments  (id PK, university_name FK, name, building, head_name, head_email)
programs     (id PK, department_id FK, name, degree_level, duration_years, language, tuition_usd)
faculty      (email PK, department_id FK, name, title, h_index, joined_year)

department_research_areas (department_id, research_area)
faculty_research_areas    (email,         research_area)
```

## The graph

| Source | Node type | Key |
|---|---|---|
| `universities` | `University` | `name` |
| `universities.country` | `Country` | `name` |
| `departments` | `Department` | `"<University>/<Department>"` |
| `programs` | `Program` | `"<University>/<Department>/<Program>"` |
| `faculty` | `Faculty` | `email` |
| research areas | `ResearchArea` | `name` |

Edges:

```
University ──BasedIn──> Country
Department ──PartOf──> University
Department ──OffersProgram──> Program
Faculty ──TeachesIn──> Department
Faculty ──Researches──> ResearchArea
Department ──HasResearchArea──> ResearchArea
```

The CSV sample defines `Advisor` (key `email`); this sample defines `Faculty` (key `email`). They are separate node *types* but share the same email keys — useful if you want to keep them as distinct labels on the graph. Rename one to match the other to collapse them.

## Self-contained SQLite seeding

`SqliteSource.EnsureSeeded(dbPath, seedSqlPath)` checks whether `data/universities.db` exists; if not, it executes the seed script against a fresh database. The connector then reads with plain `SELECT` statements through the generic `Query<T>` helper.

To swap to **PostgreSQL**:
1. Replace `Microsoft.Data.Sqlite` with `Npgsql`.
2. Change `SqliteConnection` / `SqliteDataReader` types in `SqliteSource.cs` to `Npgsql*`.
3. Drop the `EnsureSeeded` call (run the SQL against your real DB instead).
4. Update the connection string.

The SQL strings and projections in `UniversitiesIngest.cs` are unchanged.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd SqlSample
dotnet run                     # seeds data/universities.db on first run
```

Override paths with `RECIPE_DB_PATH=/tmp/u.db` and/or `RECIPE_SEED_SQL=/path/to/seed.sql`.

## Sample queries

```csharp
// All programs offered by MIT (University → Departments → Programs).
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.Department.Type, E.HasDepartment)
          .Out(N.Program.Type,    E.OffersProgram)
          .Emit("N");
```

```csharp
// Faculty researching "Artificial Intelligence" — across all universities.
return Q().StartAt(N.ResearchArea.Type, "Artificial Intelligence")
          .Out(N.Faculty.Type, E.ResearchedBy)
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students at top-5-ranked universities.
return Q().StartAt(N.University.Type)
          .Where(n => n.GetInt(N.University.Ranking) <= 5)
          .Out(N.Student.Type, E.EnrolledStudent)
          .Emit("N");
```

## Reusing this recipe

**Keep as-is**
- `SqliteSource.cs` — `EnsureSeeded` + `Query<T>` work for any SQLite database. (Swap the namespace / connection type to retarget another RDBMS as described above.)
- `Program.cs` — seed + connect + ingest + commit glue.
- `SqlSample.csproj` — Microsoft.Data.Sqlite + Curiosity.Library.

**Replace for your dataset**
- `Schema.cs` — node types + edge constants for your domain.
- `UniversitiesIngest.cs` (rename freely):
  1. The `record` types — one per SELECT projection.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — call `db.Query("SELECT …", r => new YourRow(…))` for each source table, then walk the results to emit nodes and edges. Use `Node.FromKey(...)` to reference nodes by key when their composite key is constructed elsewhere in the same run.

**Tweak in `Program.cs`**
- Connector display name.
- Default DB / seed paths.
- Drop the `EnsureSeeded` call when pointing at a real production database.
