# CSV Connector Recipe — Exploding Flattened Entities

Demonstrates the most common CSV-ingestion pattern: a single row that bundles **many entities** (a Student, their University, Department, Degree, Major, Advisor, City, plus pipe-delimited Subjects and Skills) gets exploded into typed nodes and connected with bidirectional edges.

## The CSV

`[data/students.csv](./data/students.csv)` — 10 rows, 15 columns. Each row is one student with everything about them flattened in:

```
student_id,student_name,birth_year,enrollment_year,gpa,university,department,degree,major,advisor_name,advisor_email,subjects,skills,country,city
S001,Alice Chen,2003,2022,3.85,MIT,Computer Science,BSc,Artificial Intelligence,Marcus Hill,mhill@mit.edu,Calculus|Linear Algebra|ML Foundations|Probability,Python|TensorFlow|PyTorch|Git,USA,Cambridge
```

`subjects` and `skills` are **pipe-delimited** lists (`|`) — many-per-row fields collapse to one cell.

## The graph

Each row produces:


| Source columns                                                       | Node type    | Key                           |
| -------------------------------------------------------------------- | ------------ | ----------------------------- |
| `student_id`, `student_name`, `birth_year`, `enrollment_year`, `gpa` | `Student`    | `student_id`                  |
| `university`                                                         | `University` | `name`                        |
| `department` (+ `university`)                                        | `Department` | `"<University>/<Department>"` |
| `degree`                                                             | `Degree`     | `name`                        |
| `major`                                                              | `Major`      | `name`                        |
| `subjects` (split on `                                               | `)           | `Subject` × N                 |
| `skills` (split on `                                                 | `)           | `Skill` × N                   |
| `advisor_name`, `advisor_email`                                      | `Advisor`    | `email`                       |
| `country`                                                            | `Country`    | `name`                        |
| `city` (+ `country`)                                                 | `City`       | `"<City>, <Country>"`         |


Edges (forward / reverse pairs, all bidirectional):

```
Student ──EnrolledAt──> University
Student ──BelongsToDepartment──> Department ──PartOf──> University
Student ──PursuesDegree──> Degree
Student ──HasMajor──> Major ──OfferedBy──> Department
Student ──Studies──> Subject
Student ──HasSkill──> Skill
Student ──AdvisedBy──> Advisor ──WorksIn──> Department
Student ──LivesIn──> City ──In──> Country
```

Why composite keys for `Department` and `City`? "Computer Science" exists at every university; "Cambridge" exists in USA and UK. Plain names would collapse them into one node and corrupt traversal — `"<University>/<Department>"` and `"<City>, <Country>"` keep them distinct without losing the human-readable `Name` property.

## Running

Set the environment variables and run:

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=<token from "Manage → API integrations" in your workspace>
export CURIOSITY_CONNECTOR_NAME="CSV Recipe (Students)"

cd CsvConnectorRecipe
dotnet run
```

To iterate on the parser without touching the graph:

```bash
RECIPE_SMOKE=1 dotnet run
```

To override the CSV location:

```bash
RECIPE_CSV_PATH=/path/to/your.csv dotnet run
```

## Sample queries

Open the workspace UI, then `Manage → Shell`, and try:

> The query API only has `.Out(targetType, edgeName)` — there is no `.In(...)`. Every `graph.Link(a, b, fwd, rev)` stores edges in both directions, so to "go backwards" you pass the **reverse** edge constant. For example: `Student.EnrolledAt.University` is paired with `University.EnrolledStudent.Student`, so to walk from a University to its Students you call `.Out(N.Student.Type, E.EnrolledStudent)`.

```csharp
// Sanity check — relationship counts across the entire graph.
return Q().EmitNeighborsSummary();
```

```csharp
// Every student.
return Q().StartAt(N.Student.Type).Take(50).Emit("N");
```

```csharp
// All subjects studied by Alice Chen (forward edge, Studies).
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Subject.Type, E.Studies)
          .Emit("N");
```

```csharp
// Every student at MIT (reverse-direction edge from University → Student).
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.Student.Type, E.EnrolledStudent)
          .Emit("N");
```

```csharp
// Majors pursued by students at MIT (two-hop: MIT → students → majors).
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.Student.Type, E.EnrolledStudent)
          .Out(N.Major.Type, E.HasMajor)
          .Emit("N");
```

```csharp
// Students who share the "Python" skill — useful for "find similar people".
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Student.Type, E.SkillOf)
          .Emit("N");
```

```csharp
// Faceted view: how many students each advisor has.
return Q().StartAt(N.Advisor.Type)
          .EmitNeighborsSummary();
```

## Files

```
CsvConnectorRecipe/
├── CsvConnectorRecipe.csproj   # references Curiosity.Library + CsvHelper
├── data/
│   └── students.csv            # 10 rows, the example input
└── src/
    ├── Schema.cs               # all node types + edge constants
    ├── StudentsCsvLoader.cs    # CsvHelper-backed reader → IReadOnlyList<StudentRow>
    └── Program.cs              # connect → register schema → ingest → commit → counts
```

