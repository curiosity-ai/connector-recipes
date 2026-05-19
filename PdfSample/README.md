# PDF + Office Sample — Unstructured Docs Into A RAG-Friendly Graph

A minimal connector that walks a directory of **PDF** (and **DOCX**) files, extracts text page-by-page, reads structured metadata from per-file `*.json` sidecars, chunks long pages for downstream embedding, and ingests the whole thing into a Curiosity knowledge-graph workspace. The pattern every RAG / KG-search builder needs: turn a folder of human-readable documents into a queryable graph of entities and text chunks.

This recipe ships its own sample data — a small corpus of **industrial maintenance manuals** for centrifugal pumps, AC motors, VFDs, and compressors. A Python script regenerates the corpus on demand so you can see how the source files connect to the resulting graph.

## Code shape

```
PdfSample/
├── PdfSample.csproj
├── data/
│   └── manuals/
│       ├── MAN-PMP-001.pdf + MAN-PMP-001.json   ← one pair per manual
│       ├── MAN-MTR-002.pdf + MAN-MTR-002.json
│       └── …
├── scripts/
│   └── generate_samples.py    ← regenerates the sample corpus (reportlab)
└── src/
    ├── DocumentSource.cs ← generic: PDF + DOCX text extraction + sidecar JSON
    ├── Schema.cs         ← dataset-specific: nodes + edges
    ├── ManualsIngest.cs  ← dataset-specific: metadata model + ingestion
    └── Program.cs        ← ~35-line glue: walk → extract → ingest → commit
```

`DocumentSource.cs` is the reusable piece — its `ExtractedDocument` (pages + content hash + optional metadata JSON) is the contract every downstream recipe can target.

## The sample corpus

Each manual is one PDF plus one JSON sidecar with the same basename:

```
MAN-PMP-001.pdf      ← human-readable manual (cover + one page per procedure)
MAN-PMP-001.json     ← structured metadata extracted by the SME that owns the doc
```

The PDFs describe equipment (`AHLSTAR A22-50 Centrifugal Pump`), procedures (`Replace mechanical shaft seal`), required parts (`SEAL-MS-25-A`), and authoring technicians. The JSON makes those entities and their relationships explicit so the ingester doesn't have to guess.

Cross-references give the corpus a real graph:

- `MAN-PMP-001` and `MAN-PMP-005` both reference part `GSKT-ANSI-150` — the same gasket appears across pumps.
- `MAN-MTR-002` and `MAN-PMP-001` share a bearing (`BRG-6206-2RS`).
- Technicians (`T-1001`, `T-1208`) author and perform procedures across multiple manuals.
- Manufacturers (`Sulzer`, `Grundfos`, `WEG`, `ABB`, `Atlas Copco`) supply equipment across the four plants.

## The graph

| Source | Node type | Key |
|---|---|---|
| sidecar JSON | `Manual` | `documentNumber` |
| PDF page text | `ManualPage` | `"<doc>#p<n>"` |
| chunked page text | `TextChunk` | `"<doc>#p<n>#c<i>"` |
| sidecar `equipment.*` | `Equipment` | `equipment.id` |
| sidecar `procedures[*]` | `Procedure` | `procedures[*].id` |
| sidecar `procedures[*].parts[*]` | `Part` | `partNumber` |
| sidecar `manufacturer.*` | `Manufacturer` | `name` |
| sidecar `authors[*]`, `performedBy` | `Technician` | `employeeId` |
| sidecar `hazards[*]` | `SafetyHazard` | `name` |

Edges:

```
Equipment ──DocumentedBy──> Manual
Equipment ──SuppliedBy──> Manufacturer
Manual ──AuthoredBy──> Technician
Manual ──WarnsAbout──> SafetyHazard
Manual ──HasPage──> ManualPage ──ChunkOf──> TextChunk
Manual ──Describes──> Procedure ──Maintains──> Equipment
Procedure ──PerformedBy──> Technician
Procedure ──RequiresPart──> Part
Procedure ──DescribedIn──> ManualPage
```

## Source abstraction

```csharp
public sealed record ExtractedDocument(
    string SourceFile, string SourceFileName, string ContentHash,
    IReadOnlyList<string> Pages, string? MetadataJson);

public static class DocumentSource
{
    public static IEnumerable<ExtractedDocument> Load(string root);
    public static IEnumerable<string> Chunk(string text, int chunkSize = 800, int overlap = 80);
}
```

- **PDFs** are read with **PdfPig** (100% managed, no native deps). Each page's text is preserved as a separate string in `Pages`.
- **DOCX** files are read with the Microsoft OpenXML SDK. Pages are split on `\f` form-feed markers — Word's explicit page-break.
- **Sidecar JSON**: any `<name>.json` next to a `<name>.{pdf,docx}` is read into `MetadataJson` for downstream parsing.
- **Content hash** (SHA-256 of the page text) goes onto the `Manual` node, so a downstream re-run can detect unchanged files.
- **Chunking** is provided as a helper, not enforced — keep raw pages too if your downstream search prefers them.

## Running

### 1. Regenerate the sample corpus (only needed once, or if you delete `data/`):

```bash
cd PdfSample
pip install reportlab
python scripts/generate_samples.py
```

### 2. Ingest:

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd PdfSample
dotnet run
```

Override the corpus location with `RECIPE_DOCS_ROOT=/path/to/manuals`.

## Sample queries

```csharp
// Every procedure required for equipment EQ-PMP-A22-50-01.
return Q().StartAt(N.Equipment.Type, "EQ-PMP-A22-50-01")
          .Out(N.Manual.Type,    E.DocumentedBy)
          .Out(N.Procedure.Type, E.Describes)
          .Emit("N");
```

```csharp
// Which parts does technician T-1001 need across all the procedures they perform?
return Q().StartAt(N.Technician.Type, "T-1001")
          .Out(N.Procedure.Type, E.Performs)
          .Out(N.Part.Type,      E.RequiresPart)
          .Emit("N");
```

```csharp
// Parts shared between two equipment items — the "what can I cannibalize" query.
return Q().StartAt(N.Equipment.Type, "EQ-PMP-A22-50-01")
          .Out(N.Manual.Type,    E.DocumentedBy)
          .Out(N.Procedure.Type, E.Describes)
          .Out(N.Part.Type,      E.RequiresPart)
          .Emit("Pumppart");
```

```csharp
// Full-text search lands on chunks; from there, walk back up to the procedure.
return Q().StartAt(N.TextChunk.Type)
          .Where(c => c.GetString(N.TextChunk.Content).Contains("megger"))
          .Out(N.ManualPage.Type, E.ChunkedFrom)
          .In (N.Manual.Type,     E.HasPage)
          .Out(N.Procedure.Type,  E.Describes)
          .Emit("N");
```

## Chunking and search

Pages go into the graph as full `ManualPage` nodes; each page is also chunked into smaller `TextChunk` nodes for embedding-style retrieval. The 800-character / 80-character-overlap default is a conservative starting point for most embedding models — tune the constants in `DocumentSource.Chunk` to fit your retriever.

The edges `TextChunk ──ChunkedFrom──> ManualPage ──PageOf──> Manual ──Describes──> Procedure` mean any chunk you hit in a vector search can be backtracked to a structured entity, which is the whole point of doing this in a graph rather than a flat vector store.

## Reusing this recipe

**Keep as-is**
- `DocumentSource.cs` — handles PDF + DOCX + sidecar JSON + chunking, all dataset-agnostic.
- `PdfSample.csproj` — `PdfPig`, `DocumentFormat.OpenXml`, `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — your domain's node types and edges.
- `ManualsIngest.cs`:
  1. The `ManualMetadata` (and nested) types — mirror your JSON sidecar shape.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — emit nodes, link them, and don't forget the page + chunk fan-out.

**Tweak in `Program.cs`**
- The default `docsRoot`.
- Connector display name.
- Skip the chunking pass entirely if your downstream doesn't need fine-grained chunks (delete the inner `foreach` in `ManualsIngest.Ingest`).

## Generating your own corpus

`scripts/generate_samples.py` is a working example of structured authoring: a flat Python dict per manual gets rendered into a paginated PDF *and* a matching JSON sidecar, ensuring the two stay in sync. Reuse the same pattern when you need a tractable demo corpus for a new domain — drive PDF text generation from the same dict that produces the JSON metadata.
