# JSON Connector Recipe — Skills Knowledge Graph

Companion to [`CsvConnectorRecipe`](../CsvConnectorRecipe/). The CSV recipe creates `Skill` nodes (just keys, attached to students). **This recipe deepens those same `Skill` nodes** with categories, descriptions, prerequisite links, related-skill links, and learning resources.

If you run both against the same workspace you get one cumulative graph: students-with-skills (CSV) + skill-taxonomy-with-prereqs-and-resources (here).

## Why JSON for skills

Skill metadata is naturally **nested and irregular**: a skill has a few scalar fields plus *lists of other skills* (prereqs, related) and *lists of resources*. JSON expresses that without flattening or pivot tables — one document per skill.

## The data

[`data/skills.json`](./data/skills.json) — 18 skills covering everything referenced by the CSV recipe (Python, PyTorch, Rust, Linux, …) plus a few extras.

```jsonc
{
  "name": "PyTorch",
  "category": "Library",
  "description": "Deep-learning framework with eager execution; the dominant choice for research.",
  "popularity": 9.0,
  "yearIntroduced": 2016,
  "prerequisites": ["Python"],
  "related": ["TensorFlow", "JAX"],
  "resources": [
    { "title": "PyTorch Tutorials", "url": "https://pytorch.org/tutorials/", "kind": "Tutorial" }
  ]
}
```

## Graph additions

| Source | Node type | Key |
|---|---|---|
| `name` (extends CSV's Skill with category, description, popularity, yearIntroduced) | `Skill` | `name` |
| `category` | `SkillCategory` | `name` |
| `resources[*]` | `LearningResource` | `url` |

Edges:

```
Skill ──HasCategory──> SkillCategory
LearningResource ──Teaches──> Skill
Skill ──RequiresSkill──> Skill          (prereq edge — directed)
Skill ──RelatedToSkill──> Skill         (symmetric, same name both directions)
```

## Two-pass ingestion

`Skill→Skill` edges (prereqs, related) reference skills that may not have been emitted yet when the current row is processed. The recipe handles this with **two passes**:

1. **Pass 1**: emit all `Skill`, `SkillCategory`, `LearningResource` nodes and their direct edges.
2. **Pass 2**: emit `Skill→Skill` edges using `Node.FromKey(nameof(Skill), name)` — the server resolves keys after both endpoints exist.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd SkillsJsonConnectorRecipe
dotnet run

# Parser dry-run (no graph writes):
RECIPE_SMOKE=1 dotnet run

# Override the JSON path:
RECIPE_JSON_PATH=/path/to/skills.json dotnet run
```

## Sample queries (paste into the workspace **Shell**)

> Traversal uses `.Out(targetType, edgeName)` for *both* directions — the edge constant tells you which way. Forward `RequiresSkill` and reverse `RequiredBy` are paired, so "skills that PyTorch requires" → `.Out(Skill, RequiresSkill)`; "skills that require Python" → `.Out(Skill, RequiredBy)`.

```csharp
// Sanity check — every skill type and how it's connected.
return Q().StartAt(N.Skill.Type).EmitNeighborsSummary();
```

```csharp
// Walk forward along the prereq edge: what does PyTorch require?
return Q().StartAt(N.Skill.Type, "PyTorch")
          .Out(N.Skill.Type, E.RequiresSkill)
          .Emit("N");
```

```csharp
// Walk the reverse edge: what skills require Python? (Python's "downstream" graph)
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Skill.Type, E.RequiredBy)
          .Emit("N");
```

```csharp
// All skills in the "Library" category.
return Q().StartAt(N.SkillCategory.Type, "Library")
          .Out(N.Skill.Type, E.CategoryOf)
          .Emit("N");
```

```csharp
// Combined with the CSV recipe: which students have skills that require Python?
// (Run the CSV recipe first so Student nodes exist.)
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Skill.Type, E.RequiredBy)     // skills that require Python
          .Out(N.Student.Type, E.SkillOf)      // students with those skills
          .Emit("N");
```

## Files

```
SkillsJsonConnectorRecipe/
├── SkillsJsonConnectorRecipe.csproj
├── data/skills.json
└── src/
    ├── Schema.cs              # Skill (extended) + SkillCategory + LearningResource
    ├── SkillsJsonLoader.cs    # System.Text.Json deserializer
    └── Program.cs             # two-pass ingestion (nodes → cross-skill edges)
```
