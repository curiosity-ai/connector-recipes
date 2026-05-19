# Connector Recipes

A library of self-contained, runnable examples for ingesting common source formats — CSV, JSON, S3, SQL, REST APIs, PostgreSQL/MySQL, MongoDB, GitHub GraphQL, PDFs, sitemap crawls, Kafka streams, Parquet/Avro, RSS/Atom — into a [Curiosity](https://curiosity.ai) knowledge-graph workspace.

Every recipe (except the PDF one, which uses an industrial-maintenance corpus) targets the **same imaginary domain** — a small academic graph of students, universities, subjects, and skills — and shows how each connector can **cumulatively build one graph**, merging on shared keys.

Each recipe is structured to make reuse explicit: one generic source-format file you keep, one schema file you replace, one ingestion file you replace, and a tiny `Program.cs` that wires them together.

---

## Table of contents

1. [What is a data connector?](#what-is-a-data-connector)
2. [Why write one?](#why-write-one)
3. [Prerequisites](#prerequisites)
4. [The samples](#the-samples)
5. [Why multiple connectors for one graph?](#why-multiple-connectors-for-one-graph)
6. [Code shape (shared across samples)](#code-shape-shared-across-samples)
7. [Running them](#running-them)
8. [Starting your own from a sample](#starting-your-own-from-a-sample)
9. [License](#license)

---

## What is a data connector?

A **data connector** is a small program that reads data from a source — a file, an API, a database — and writes it into a Curiosity workspace as a **knowledge graph**: typed **nodes** (entities like *Customer*, *Product*, *SupportCase*) connected by typed **edges** (relationships like *Purchased*, *AssignedTo*, *DependsOn*).

Once your data lives in the graph, the workspace gives you:

- Typed search and filtering across all entity types at once.
- Visual graph exploration (click a node, see its neighbors).
- A query language for traversal, aggregation, and ad-hoc analysis.
- Hooks for natural-language search, embeddings, and conversational interfaces.

## Why write one?

Most useful when:

- Your data is **relational by nature** but lives in formats that hide those relationships (CSVs, REST endpoints, scattered JSON exports).
- You want to **unify multiple sources** behind one searchable interface.
- You want **non-technical users to explore the data** without SQL.
- You're prototyping a knowledge-graph application and need real data in fast.

A connector is **not** a streaming pipeline replacement. It's an ingestion job — run it on a schedule (cron, CI) or on demand.

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report `10.x`).
- A **Curiosity workspace** you can connect to. Local workspaces typically run at `http://localhost:8080/`.
- An **API token** from that workspace: `Manage → API integrations → Create API Token`.

## The samples

### Core samples (academic graph)

| Sample | Source format | What it owns in the graph |
|---|---|---|
| [`CsvSample`](./CsvSample/) | CSV | Student-centric backbone |
| [`JsonSample`](./JsonSample/) | JSON | Skill taxonomy + learning resources |
| [`S3Sample`](./S3Sample/) | S3 (local fallback) | Subjects, topics, books, authors |
| [`SqlSample`](./SqlSample/) | SQLite | Universities, departments, programs, faculty |
| [`RestApiSample`](./RestApiSample/) | REST API (paginated, token auth) | Courses, terms, instructors |
| [`PostgresSample`](./PostgresSample/) | PostgreSQL / MySQL (watermark sync) | Research grants, funding agencies |
| [`MongoSample`](./MongoSample/) | MongoDB (change streams) | Student profiles, internships, projects, companies |
| [`GitHubSample`](./GitHubSample/) | GitHub GraphQL | Repos, issues, PRs, reviews, users, topic tags |
| [`SitemapSample`](./SitemapSample/) | Web scraping (sitemap.xml) | University web pages, sections, tags |
| [`KafkaSample`](./KafkaSample/) | Kafka / CDC stream | Live enrollment events, grades, statuses |
| [`ParquetSample`](./ParquetSample/) | Parquet / Avro | Course grades on a data lake |
| [`RssSample`](./RssSample/) | RSS / Atom feeds | University news items, authors, categories |

### Domain sample (industrial)

| Sample | Source format | What it owns in the graph |
|---|---|---|
| [`PdfSample`](./PdfSample/) | PDF + DOCX + JSON sidecars | Industrial maintenance manuals — equipment, procedures, parts, manufacturers, technicians |

Each recipe is independent, re-runnable, and self-contained — including its own data folder.

## Why multiple connectors for one graph?

Real-world knowledge rarely lives in one place. The information you'd want about a single concept — say, a university — is spread across:

- A **spreadsheet** maintained by an admissions office.
- A **JSON dump** from a skill-tagging service.
- An **S3 bucket** of course materials.
- A **relational database** of institutional metadata.

The pattern these samples demonstrate: **write one connector per source, and let them merge on shared keys.** When two connectors emit a node with the same `[Key]` (e.g. `Skill.Name = "Python"`), the workspace **doesn't duplicate** — it merges. New `[Property]` fields stack onto the same node, new edges attach to it, and queries can traverse across data that originally lived in completely different systems.

| Shared key | Seeded by | Enriched by |
|---|---|---|
| `Skill.Name` (e.g. "Python") | `CsvSample` (linked to Students) | `JsonSample` (category, prereqs, learning resources), `MongoSample` (per-student usage), `GitHubSample` (topic tags) |
| `Subject.Name` (e.g. "Calculus") | `CsvSample` (linked to Students) | `S3Sample` (level, topics, books), `RestApiSample` (linked to Courses), `ParquetSample` (linked to Grades) |
| `University.Name` (e.g. "MIT") | `CsvSample` (linked to Students) | `SqlSample` (founded year, ranking), `SitemapSample` (linked to web pages), `RssSample` (linked to news) |
| `Department.Id` (composite) | `CsvSample` | `SqlSample` (head contact + building) |
| `Country.Name` | `CsvSample` (via City) | `SqlSample`, `PostgresSample` (via FundingAgency) |
| `Faculty.Email` / `Advisor.Email` | `CsvSample` (Advisor), `SqlSample` (Faculty) | `RestApiSample` (Instructors), `PostgresSample` (Grant PIs) |
| `Student.Id` | `CsvSample` | `MongoSample` (profile, internships, projects), `KafkaSample` (live enrollments), `ParquetSample` (grades) |
| `Course.Code` | `RestApiSample` | `KafkaSample` (enrollments), `ParquetSample` (grades) |
| `Term.Name` | `RestApiSample` | `KafkaSample` (enrollments), `ParquetSample` (grades) |

## Code shape (shared across samples)

All four projects share the same three-file layout, designed so the reusable part stays separate from the part you rewrite for your data:

```
<Sample>/
├── <Sample>.csproj
├── data/...                    # the sample input data
└── src/
    ├── <Format>Source.cs       ← generic: CSV / JSON / S3 / SQLite reader
    ├── Schema.cs               ← dataset-specific: [Node] / [Key] / [Property] / edge constants
    ├── <Domain>Ingest.cs       ← dataset-specific: row model + RegisterSchemaAsync + Ingest
    └── Program.cs              ← ~25-line glue: load → register → ingest → commit
```

All code lives in a single namespace: `Curiosity.Library.Recipes`.

## Running them

Each sample is an independent .NET 10 console app. From the repo root:

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=<token from "Manage → API integrations">

# Run any subset, in any order — they merge automatically on shared keys.
dotnet run --project CsvSample
dotnet run --project JsonSample
dotnet run --project S3Sample
dotnet run --project SqlSample

# The newer recipes follow the same convention:
dotnet run --project RestApiSample
dotnet run --project PostgresSample        # needs RECIPE_DB_URL — see its README
dotnet run --project MongoSample
dotnet run --project GitHubSample
dotnet run --project PdfSample             # run scripts/generate_samples.py first
dotnet run --project SitemapSample
dotnet run --project KafkaSample
dotnet run --project ParquetSample         # run scripts/generate_samples.py first
dotnet run --project RssSample
```

Verify the result in the workspace UI → `Manage → Shell`:

```csharp
return Q().EmitNeighborsSummary();
```

For source-format-specific details (schema choices, sample queries, environment variables) see each sample's own README.

## Starting your own from a sample

Pick the sample whose source format matches yours and follow its **"Reusing this recipe"** section. Across all four, the recipe is the same:

1. **Copy the project folder** under a new name.
2. **Keep `<Format>Source.cs` as-is** — it's dataset-agnostic.
3. **Rewrite `Schema.cs`** — define your node types (with `[Key]` / `[Property]`) and edge constants.
4. **Rewrite `<Domain>Ingest.cs`** — three things: a POCO that mirrors a row/document, `RegisterSchemaAsync` listing every node type, and `Ingest` that emits nodes (`graph.TryAdd` / `graph.AddOrUpdate`) and edges (`graph.Link(a, b, fwd, rev)`).
5. **Tweak `Program.cs`** — connector display name, default data path, and the type passed to the loader.

For composite keys (e.g. `"<University>/<Department>"`) build the string in the ingest method and assign it to a `[Key]` property. To reference a node whose key is built elsewhere in the run, use `Node.FromKey(nameof(Nodes.YourType), keyValue)`.

## License

MIT — see `LICENSE`.
