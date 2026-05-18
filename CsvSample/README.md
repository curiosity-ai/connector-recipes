# CSV Sample — Exploding Flat Rows Into Typed Nodes

A minimal connector that reads a flat CSV file and ingests it into a Curiosity knowledge-graph workspace. Each row carries many entities at once (Student + University + Department + Advisor + …); the recipe explodes that row into typed nodes and bidirectional edges.

## Code shape

```
CsvSample/
├── CsvSample.csproj
├── data/students.csv
└── src/
    ├── CsvSource.cs        ← generic: reads any CSV into typed rows
    ├── Schema.cs           ← dataset-specific: nodes + edges
    ├── StudentsIngest.cs   ← dataset-specific: row model + ingestion
    └── Program.cs          ← ~25-line glue: load → register → ingest → commit
```

`CsvSource.cs` is the reusable piece. `Schema.cs` and `StudentsIngest.cs` are what you'd rewrite to adapt the recipe to your own CSV.

## The data

[`data/students.csv`](./data/students.csv) — 10 rows, 15 columns. Each row is one student flattened with their university, department, degree, advisor, city, country, and pipe-delimited subjects + skills:

```
student_id,student_name,birth_year,...,subjects,skills,country,city
S001,Alice Chen,2003,...,Calculus|Linear Algebra|ML Foundations,Python|TensorFlow|PyTorch,USA,Cambridge
```

## The graph

| Source columns | Node type | Key |
|---|---|---|
| `student_*`, `gpa` | `Student` | `student_id` |
| `university` | `University` | `name` |
| `department` (+ university) | `Department` | `"<University>/<Department>"` |
| `degree` | `Degree` | `name` |
| `major` | `Major` | `name` |
| `subjects` (pipe-split) | `Subject` × N | `name` |
| `skills` (pipe-split) | `Skill` × N | `name` |
| `advisor_*` | `Advisor` | `email` |
| `country` | `Country` | `name` |
| `city` (+ country) | `City` | `"<City>, <Country>"` |

Composite keys (`Department`, `City`) keep same-named entities distinct across parents.

## Running

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=<token from "Manage → API integrations">

cd CsvSample
dotnet run
```

Override the CSV path with `RECIPE_CSV_PATH=/path/to/your.csv`.

## Sample queries

Paste into the workspace **Manage → Shell**.

> Traversal is always `.Out(targetType, edgeName)` — to walk an edge "backwards" pass the paired reverse-edge constant.

```csharp
return Q().EmitNeighborsSummary();
```

```csharp
// Subjects studied by S001.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Subject.Type, E.Studies)
          .Emit("N");
```

```csharp
// Students at MIT (reverse-edge traversal).
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.Student.Type, E.EnrolledStudent)
          .Emit("N");
```

```csharp
// Students who share the "Python" skill.
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Student.Type, E.SkillOf)
          .Emit("N");
```

## Reusing this recipe

The split between `CsvSource.cs` and `Schema.cs` + `StudentsIngest.cs` is deliberate: one is reusable, the others are the bits you rewrite.

**Keep as-is**
- `CsvSource.cs` — generic CSV → POCO reader.
- `Program.cs` — the load / register / loop / commit glue.
- `CsvSample.csproj` — package references (Curiosity.Library, CsvHelper).

**Replace for your dataset**
- `Schema.cs` — define your node types (with `[Key]` / `[Property]`) and edge constants. Use composite keys when the same name can appear under different parents.
- `StudentsIngest.cs` (rename freely) — three things:
  1. The `Row` class: one `[Name("column")]` property per CSV column you read.
  2. `RegisterSchemaAsync`: list each node type plus `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest`: emit nodes with `graph.TryAdd` / `graph.AddOrUpdate` and connect them with `graph.Link(a, b, forwardEdge, reverseEdge)`.

**Tweak in `Program.cs`**
- The default CSV filename (`students.csv` → your file).
- The connector display name (`"CSV Sample (Students)"` → yours).
- The type passed to `CsvSource.Load<…>()` and the call to your renamed `*Ingest` static class.

When this connector runs against the same workspace as the other samples, shared keys (`Skill.Name`, `Subject.Name`, `University.Name`, `Advisor.Email`) merge automatically.
