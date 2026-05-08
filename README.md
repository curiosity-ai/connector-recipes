# Connector Recipes

A library of self-contained, runnable examples for ingesting common source formats — CSV, JSON, S3, SQL — into a [Curiosity](https://curiosity.ai) knowledge-graph workspace.

Every recipe in this repo targets the **same imaginary domain** — a small academic knowledge graph of students, universities, subjects, and skills — and demonstrates how four connectors reading from four different sources can **cumulatively build one graph**, each one deepening a different slice of it.

---

## Table of contents

1. [What is a data connector?](#what-is-a-data-connector)
2. [Why write one?](#why-write-one)
3. [Prerequisites](#prerequisites)
4. [Why multiple connectors for one graph?](#why-multiple-connectors-for-one-graph)
5. [The example domain](#the-example-domain)
6. [The recipes](#the-recipes)
7. [Running them](#running-them)
8. [License](#license)

---

## What is a data connector?

A **data connector** is a small program that reads data from a source — a file, an API, a database — and writes it into a Curiosity workspace as a **knowledge graph**: a network of typed **nodes** (entities like *Customer*, *Product*, *SupportCase*) connected by typed **edges** (relationships like *Purchased*, *AssignedTo*, *DependsOn*).

Once your data lives in the graph, the workspace gives you:

- Typed search and filtering across all entity types at once.
- Visual graph exploration (click a node, see its neighbors).
- A query language for traversal, aggregation, and ad-hoc analysis.
- Hooks for natural-language search, embeddings, and conversational interfaces.

A connector is the bridge from **your data** to **that experience**.

## Why write one?

Most useful when:

- Your data is **relational by nature** but lives in formats that make those relationships hard to follow (CSVs, log files, REST endpoints, scattered JSON exports).
- You want to **unify multiple sources** behind one searchable interface — e.g. customers from a CRM, tickets from a support system, devices from an inventory database, all linked.
- You want **non-technical users to explore the data** without writing SQL or building dashboards.
- You're prototyping a knowledge-graph application and need real data in fast.

A connector is **not** a streaming pipeline replacement. It's an ingestion job — run it on a schedule (cron, CI, a workflow runner) or on demand.

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` should report `10.x`).
- A **Curiosity workspace** you can connect to. Local workspaces typically run at `http://localhost:8080/`; hosted workspaces have their own URL.
- An **API token** from that workspace. Generate one in the workspace UI:
  `Manage → API integrations → Create API Token`. Copy the token value — it's shown only once.

That's it. Everything else comes from NuGet.

---

## Why multiple connectors for one graph?

Real-world knowledge rarely lives in one place. The information you'd want about a single concept — say, a university — is spread across:

- A **spreadsheet** maintained by an admissions office (rows of students with their majors).
- A **JSON dump** from some skill-tagging service (a taxonomy of programming languages, frameworks, tools).
- An **S3 bucket** of course materials (one document per subject, with topics and recommended books).
- A **relational database** of institutional metadata (universities, departments, programs, faculty).

Each source covers a different slice and uses a different format. None of them on their own gives you the whole picture.

The pattern this repo demonstrates: **write one connector per source, and let them merge automatically on shared keys.** When two connectors emit a node with the same `[Key]` (e.g. `Skill.Name = "Python"` or `University.Name = "MIT"`), the workspace **doesn't duplicate** — it merges. New `[Property]` fields stack onto the same node, new edges attach to it, and queries can traverse across data that originally lived in completely different systems.

The result: one graph, four data sources, and traversals like *"show me students at top-5-ranked universities whose skills require Python"* that cross every source boundary.

This is the right pattern when:

- You have **stable identifiers** that appear in more than one source (an email, an ID, a name that's effectively unique in context).
- You want each source's connector to stay **independent and re-runnable** — owned by whichever team owns that source.
- You expect the graph to **grow over time** by adding more connectors, not by rewriting one big one.

## The example domain

All four recipes target a small academic knowledge graph. The entities and how they relate:

```
                              Country
                                 ▲
                                 │ BasedIn / In
                                 │
        ┌─────────► University ──┴──► City ◄────── Student ──► Skill ──► SkillCategory
        │             │                              │           │
        │ PartOf      │ HasDepartment                │ HasMajor  │ Teaches (LearningResource)
        │             ▼                              │           │
        │          Department ──► Program            │           │ RequiresSkill (Skill→Skill)
        │             │  │                           │           │
        │             │  └─► HasFaculty ──► Faculty ─┘           │
        │             │                       │                  │
        │             └─► HasResearchArea ──► ResearchArea       │
        │                                                        │
        │ Studies                                                │
        ▼                                                        │
     Subject ──► Topic                                           │
        │                                                        │
        ├──► Book ──► Author                                     │
        │                                                        │
        └────────────────────────────────────────────────────────┘
```

The main entities, grouped by what they represent:

- **People** — `Student`, `Advisor`/`Faculty` (linked by email).
- **Where they are** — `University`, `Department`, `City`, `Country`.
- **What they pursue** — `Degree` (BSc / MSc / PhD), `Major`, `Program` (full degree program with language, duration, tuition).
- **What they learn** — `Subject`, `Topic` (within a subject), `Skill`, `SkillCategory` (Language / Library / Tool / Framework).
- **Materials** — `Book`, `Author`, `LearningResource` (tutorials, online courses).
- **What faculty research** — `ResearchArea`.

Crucially, **none of the four recipes creates all of these on its own**. Each one is responsible for a slice. The slices fit together through shared keys.

## The recipes

| Recipe | Source format | What slice of the graph it owns |
|---|---|---|
| [`CsvConnectorRecipe`](./CsvConnectorRecipe/) | CSV | Student-centric backbone |
| [`SkillsJsonConnectorRecipe`](./SkillsJsonConnectorRecipe/) | JSON | Skill taxonomy & resources |
| [`SubjectsS3ConnectorRecipe`](./SubjectsS3ConnectorRecipe/) | S3 (with local fallback) | Subject content & reading lists |
| [`UniversitiesSqlConnectorRecipe`](./UniversitiesSqlConnectorRecipe/) | SQLite | Institutional metadata |

### `CsvConnectorRecipe` — the backbone

Reads a flat `students.csv` where each row carries a Student plus their University, Department, Degree, Major, Advisor, City, Country, and pipe-delimited Subjects and Skills. The recipe **explodes** that flat row into typed nodes and connects them with bidirectional edges. After it runs, the graph has every Student in the dataset and a thin first reference to the Skill / Subject / University / Advisor nodes the other recipes will deepen.

### `SkillsJsonConnectorRecipe` — the skill graph

Reads a `skills.json` document where each skill carries a category, description, popularity score, year of introduction, and lists of *prerequisite* and *related* skills. It **deepens** the Skill nodes the CSV connector seeded with rich properties, and adds two new entities — `SkillCategory` and `LearningResource` — plus the cross-skill edges (`RequiresSkill` for prereqs, `RelatedToSkill` for similarity). After running both, you can ask the graph things like *"which students have skills that transitively require Python?"*.

### `SubjectsS3ConnectorRecipe` — subjects and books

Reads from an S3 bucket organised as `subjects/<name>.json` and `books/<isbn>.json` (with a local-filesystem fallback so the recipe runs without AWS credentials). Each subject document references its books by ISBN, decoupling subject metadata from book metadata so the same book can appear under multiple subjects. The connector **deepens** the Subject nodes the CSV connector seeded with topics and recommended reading, and introduces three new entities: `Topic`, `Book`, and `Author`.

### `UniversitiesSqlConnectorRecipe` — institutional metadata

Reads from a SQLite database (auto-seeded from a `seed.sql` script on first run; the recipe documents how to swap in PostgreSQL/SQL Server). Joins universities → departments → programs → faculty → research areas across six tables. **Deepens** the University and Department nodes the CSV connector seeded with founded year, world ranking, head-of-department contacts, and building names; introduces three new entities: `Program`, `Faculty` (extending CSV's `Advisor` by email key), and `ResearchArea`.

### How the slices fit together

Run the recipes in any order — the merge happens by key, not by sequence:

| Shared key | Seeded by | Enriched by |
|---|---|---|
| `Skill.Name` (e.g. "Python") | CSV (linked to Students) | JSON (category, prereqs, related, learning resources) |
| `Subject.Name` (e.g. "Calculus") | CSV (linked to Students) | S3 (level, topics, recommended books, authors) |
| `University.Name` (e.g. "MIT") | CSV (linked to Students) | SQL (founded year, ranking, programs, faculty, research areas) |
| `Department` composite key | CSV | SQL (head contact + building) |
| `Country.Name` | CSV (via City) | SQL (links University → Country directly) |

Each recipe's README has 4–5 sample `Q()` queries — including cross-recipe traversals that only become possible once two or more recipes have populated the same workspace.

## Running them

Each recipe is an independent .NET 10 console app. From the repo root:

```bash
export CURIOSITY_URL=http://localhost:8080/
export CURIOSITY_API_TOKEN=<your token from "Manage → API integrations">

# Run any subset, in any order — they merge automatically on shared keys.
dotnet run --project CsvConnectorRecipe
dotnet run --project SkillsJsonConnectorRecipe
dotnet run --project SubjectsS3ConnectorRecipe
dotnet run --project UniversitiesSqlConnectorRecipe
```

To verify the result, open the workspace UI → `Manage → Shell` and try:

```csharp
return Q().EmitNeighborsSummary();
```

For source-format-specific details (schema choices, pitfalls, sample queries) see each recipe's own README.

## License

MIT — see `LICENSE`.
