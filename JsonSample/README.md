# JSON Sample — Nested Documents With Cross-References

A minimal connector that reads a JSON array file and ingests it into a Curiosity knowledge-graph workspace. The example uses skills with prerequisite + related-skill cross-links; ingestion is done in two passes so document order doesn't matter.

## Code shape

```
JsonSample/
├── JsonSample.csproj
├── data/skills.json
└── src/
    ├── JsonSource.cs       ← generic: reads any JSON file (array or object)
    ├── Schema.cs           ← dataset-specific: nodes + edges
    ├── SkillsIngest.cs     ← dataset-specific: document model + ingestion
    └── Program.cs          ← ~25-line glue: load → register → ingest → commit
```

`JsonSource.cs` is the reusable piece. `Schema.cs` and `SkillsIngest.cs` are what you'd rewrite to adapt the recipe to your own JSON.

## The data

[`data/skills.json`](./data/skills.json) — a top-level array of skill documents, each with nested lists for prerequisites, related skills, and learning resources:

```jsonc
{
  "name": "PyTorch",
  "category": "Library",
  "description": "Deep-learning framework with eager execution.",
  "popularity": 9.0,
  "yearIntroduced": 2016,
  "prerequisites": ["Python"],
  "related": ["TensorFlow", "JAX"],
  "resources": [
    { "title": "PyTorch Tutorials", "url": "https://pytorch.org/tutorials/", "kind": "Tutorial" }
  ]
}
```

## The graph

| Source field | Node type | Key |
|---|---|---|
| `name` (+ description, popularity, yearIntroduced) | `Skill` | `name` |
| `category` | `SkillCategory` | `name` |
| `resources[*]` | `LearningResource` | `url` |

Edges:

```
Skill ──HasCategory──> SkillCategory
LearningResource ──Teaches──> Skill
Skill ──RequiresSkill──> Skill       (prereq, directed)
Skill ──RelatedToSkill──> Skill      (symmetric, same name both directions)
```

## Two-pass ingestion

Skill → Skill edges (prereqs, related) reference skills that may not have been emitted yet when the current document is processed. The recipe handles this with two passes:

1. **Pass 1** — emit every `Skill`, `SkillCategory`, `LearningResource` node and its direct edges.
2. **Pass 2** — emit `Skill → Skill` edges using `Node.FromKey(nameof(Skill), name)`; the server resolves keys after both endpoints exist.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd JsonSample
dotnet run
```

Override the JSON path with `RECIPE_JSON_PATH=/path/to/your.json`.

## Sample queries

```csharp
return Q().StartAt(N.Skill.Type).EmitNeighborsSummary();
```

```csharp
// What does PyTorch require?
return Q().StartAt(N.Skill.Type, "PyTorch")
          .Out(N.Skill.Type, E.RequiresSkill)
          .Emit("N");
```

```csharp
// What skills require Python? (reverse-edge: Python's downstream graph)
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Skill.Type, E.RequiredBy)
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students with skills that require Python.
return Q().StartAt(N.Skill.Type, "Python")
          .Out(N.Skill.Type,   E.RequiredBy)
          .Out(N.Student.Type, E.SkillOf)
          .Emit("N");
```

## Reusing this recipe

**Keep as-is**
- `JsonSource.cs` — generic JSON file → typed object reader (`LoadArray<T>` for top-level arrays, `LoadObject<T>` for one-doc-per-file).
- `Program.cs` — the load / register / ingest / commit glue.
- `JsonSample.csproj` — the package references.

**Replace for your dataset**
- `Schema.cs` — node types + edge constants for your domain.
- `SkillsIngest.cs` (rename freely):
  1. The `*Doc` classes: one `[JsonPropertyName("…")]` property per JSON field you read.
  2. `RegisterSchemaAsync`: list each node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest`: emit nodes per document, then handle cross-document edges in a second pass with `Node.FromKey(...)` if your data has forward references.

**Tweak in `Program.cs`**
- Default filename (`skills.json` → yours).
- Connector display name.
- Use `JsonSource.LoadArray<T>` vs `LoadObject<T>` depending on the file shape.

If a single JSON file contains one document instead of an array, swap the call to `JsonSource.LoadObject<…>` and drop the per-element loop.
