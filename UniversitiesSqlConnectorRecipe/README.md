# SQL Connector Recipe — Universities, Programs & Faculty

Companion to [`CsvConnectorRecipe`](../CsvConnectorRecipe/), [`SkillsJsonConnectorRecipe`](../SkillsJsonConnectorRecipe/), and [`SubjectsS3ConnectorRecipe`](../SubjectsS3ConnectorRecipe/). The CSV recipe creates `University`, `Department`, and `Advisor` nodes (just keys). **This recipe deepens those same nodes** with institutional metadata pulled from a relational source: founded year, ranking, head-of-department contacts, programs offered, faculty profiles, and research areas.

## Why SQL for institutions

University data is the most **tabular** slice of the academic graph: rigid schema, foreign-key relationships between universities → departments → programs → faculty. SQL is exactly the right shape for that — and the connector demonstrates the pattern of joining many tables into one knowledge-graph projection. The recipe ships a SQLite database for portability; swapping in `Npgsql` or `Microsoft.Data.SqlClient` is a one-line change.

## The data

[`data/seed.sql`](./data/seed.sql) — a SQLite seed script with six tables:

```
universities (name PK, country, founded_year, ranking, students_count, website)
departments  (id PK, university_name FK, name, building, head_name, head_email)
programs     (id PK, department_id FK, name, degree_level, duration_years, language, tuition_usd)
faculty      (email PK, department_id FK, name, title, h_index, joined_year)

department_research_areas (department_id, research_area)        -- many-to-many
faculty_research_areas    (email,         research_area)        -- many-to-many
```

Universities include all five from the CSV recipe (MIT, Stanford, ETH Zurich, Tokyo, USP) plus Cambridge and Oxford — so the SQL recipe also stands alone.

## Graph additions

| Source | Node type | Key |
|---|---|---|
| `universities` (extends CSV) | `University` | `name` |
| `universities.country` | `Country` | `name` |
| `departments` (extends CSV) | `Department` | `"<University>/<Department>"` |
| `programs` | `Program` | `"<University>/<Department>/<Program>"` |
| `faculty` (extends CSV's `Advisor` — same email keys) | `Faculty` | `email` |
| research areas | `ResearchArea` | `name` |

> Note: the SQL recipe defines a `Faculty` node, while the CSV recipe defines an `Advisor` node — but **both use `email` as their key**. They are separate node *types* on the graph, even though many advisors will also appear as faculty. If you want them collapsed into one type, rename `Advisor` → `Faculty` in the CSV recipe's schema before running it; the email keys then merge.

Edges:

```
University ──BasedIn──> Country
Department ──PartOf──> University
Department ──OffersProgram──> Program
Faculty ──TeachesIn──> Department
Faculty ──Researches──> ResearchArea
Department ──HasResearchArea──> ResearchArea
```

`PartOf` / `HasDepartment` deliberately reuses the same edge constants as the CSV recipe so the same relationship doesn't get double-named on the graph.

## Self-contained SQLite seeding

`UniversitiesSqlLoader.EnsureSeeded(dbPath, seedSqlPath)` checks whether `data/universities.db` exists; if not, it creates it from `data/seed.sql`. The connector then reads with plain `SELECT` statements through `Microsoft.Data.Sqlite`. This keeps the recipe runnable with zero setup.

To swap to **PostgreSQL**: replace the `Microsoft.Data.Sqlite` reference with `Npgsql`, change `SqliteConnection` to `NpgsqlConnection`, drop `EnsureSeeded` (run the SQL against your real DB instead), and update the connection string. The query bodies are unchanged.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd UniversitiesSqlConnectorRecipe
dotnet run                     # seeds data/universities.db on first run

# Parser dry-run:
RECIPE_SMOKE=1 dotnet run

# Override DB or seed paths:
RECIPE_DB_PATH=/tmp/u.db RECIPE_SEED_SQL=/path/to/seed.sql dotnet run
```

## Sample queries (paste into the workspace **Shell**)

> Traversal is always `.Out(targetType, edgeName)`. To go in the "reverse" direction (e.g. from University to its Departments), pass the reverse-edge constant — `HasDepartment` is paired with `PartOf`, `HasFaculty` with `TeachesIn`, `ResearchedBy` with `Researches`.

```csharp
// Sanity check.
return Q().StartAt(N.University.Type).EmitNeighborsSummary();
```

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
// All faculty at MIT (University → Departments → Faculty).
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.Department.Type, E.HasDepartment)
          .Out(N.Faculty.Type,    E.HasFaculty)
          .Emit("N");
```

```csharp
// Combined with the CSV recipe: which students are at universities ranked
// in the global top 5? Filter on a property, then walk the reverse edge.
return Q().StartAt(N.University.Type)
          .Where(n => n.GetInt(N.University.Ranking) <= 5)
          .Out(N.Student.Type, E.EnrolledStudent)
          .Emit("N");
```

## Files

```
UniversitiesSqlConnectorRecipe/
├── UniversitiesSqlConnectorRecipe.csproj    # Microsoft.Data.Sqlite + Curiosity.Library
├── data/
│   ├── seed.sql                             # the schema + sample data (universities/departments/programs/faculty/research areas)
│   └── universities.db                      # generated by EnsureSeeded on first run
└── src/
    ├── Schema.cs                            # University/Department (extended) + Program/Faculty/ResearchArea (new)
    ├── UniversitiesSqlLoader.cs             # SQLite reader: seeds DB if absent + 4 SELECT-backed loaders
    └── Program.cs                           # connect → schema → ingest → counts
```
