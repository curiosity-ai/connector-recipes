# Parquet / Avro Sample — Columnar Lake Files Into The Graph

A minimal connector that reads a **Parquet** (or **Avro**) file row-group-by-row-group, projects only the columns the ingestion needs, and writes the rows into a Curiosity knowledge-graph workspace as a typed graph. This is the columnar batch sibling of the CSV recipe — same logical role, very different on-disk format.

## Why columnar deserves its own recipe

CSV is row-oriented and small-data; reading every byte to get every row is fine. The moment files grow past tens of MB or columns past ~50, the standard analytics pattern is to store them as Parquet (compressed, columnar, splittable) or Avro (compact, schema-evolving, streaming-friendly) and to **read only the columns you need**. That's what a connector against a data lake actually looks like.

This recipe ships:

- A `ParquetSource` that streams row groups one at a time (bounded memory regardless of file size).
- An `AvroSource` that streams records the same way over an `.avro` file.
- A common `ColumnarRow` shape so ingestion code never knows which format it came from.
- Column projection up front — the file reader skips columns the ingestion doesn't list.

## Code shape

```
ParquetSample/
├── ParquetSample.csproj
├── data/
│   ├── grades.parquet            ← 165 rows of course grades
│   └── grades.avro               ← same rows, Avro format
├── scripts/
│   └── generate_samples.py       ← regenerates the data files (pandas + pyarrow + fastavro)
└── src/
    ├── ColumnarSource.cs ← generic: IColumnarSource + Parquet + Avro readers
    ├── Schema.cs         ← dataset-specific: nodes + edges
    ├── GradesIngest.cs   ← dataset-specific: column list + ingestion
    └── Program.cs        ← ~30-line glue: pick driver → read → ingest → commit
```

`ColumnarSource.cs` is the reusable piece. `Schema.cs` and `GradesIngest.cs` are what you rewrite for your own columns.

## The data

165 rows of course grades for 30 students across two terms. Every column matches the vocabulary the other recipes already use, so grades attach to existing students, courses, subjects, and terms without manual reconciliation.

| Column | Sample | Used by |
|---|---|---|
| `student_id` | `S014` | shared with CSV / Mongo / Kafka |
| `course_code` | `CS344` | shared with REST API / Kafka |
| `subject` | `Machine Learning` | shared with CSV / S3 / REST API |
| `term` | `Fall 2024` | shared with REST API / Kafka |
| `letter_grade` | `B+` | new |
| `gpa_points` | `3.3` | new |
| `credit_hours` | `4` | new |

## The graph

| Source column | Node type | Key |
|---|---|---|
| `student_id` | `Student` | id (shared) |
| `course_code` | `Course` | code (shared with REST/Kafka) |
| `subject` | `Subject` | name (shared) |
| `term` | `Term` | name (shared with REST/Kafka) |
| composite | `Grade` | `"<student>/<course>/<term>"` |

Edges:

```
Student ──Received──> Grade ──ForCourse──> Course ──CoversSubject──> Subject
                              ──DuringTerm──> Term
```

## Source abstraction

```csharp
public sealed record ColumnarRow(IReadOnlyDictionary<string, object?> Values)
{
    public T? Get<T>(string name);
}

public interface IColumnarSource
{
    IAsyncEnumerable<ColumnarRow> ReadAsync(string path, IReadOnlyList<string>? columns = null);
}
```

- `ParquetSource` — `Parquet.Net` row-group reader. Each row group is decompressed and decoded once; rows are yielded one at a time from the in-memory columns. Memory peak is bounded by row-group size (typically 100k rows), not by file size.
- `AvroSource` — `Apache.Avro` `DataFileReader<GenericRecord>` block-by-block iteration. Same per-record yield contract.
- Column projection (`columns = ["a", "b", "c"]`) tells the reader to skip every other column, which is the single biggest performance win on wide files.

Ingestion calls `row.Get<T>(name)` for each field — the dictionary holds boxed values, which `Get<T>` either casts directly or `Convert.ChangeType`'s into the requested type.

## Running

```bash
# 1. Generate the data files (once):
cd ParquetSample
pip install pandas pyarrow fastavro
python scripts/generate_samples.py

# 2. Ingest:
export CURIOSITY_API_TOKEN=<workspace token>
dotnet run                                       # Parquet (default)
RECIPE_DATA_PATH=data/grades.avro dotnet run     # Avro
```

Point at your own file with `RECIPE_DATA_PATH=/path/to/file.parquet`. The driver picks itself on file extension.

## Sample queries

```csharp
// S001's transcript across both terms.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Grade.Type, E.Received)
          .Emit("N");
```

```csharp
// Combined with the REST API sample: average grade per instructor — Faculty
// teaches Course, Course received-by Grade.
return Q().StartAt(N.Faculty.Type)
          .Out(N.Course.Type, E.Teaches)
          .Out(N.Grade.Type,  E.CourseOf)
          .EmitNeighborsSummary();
```

```csharp
// Combined with the Kafka sample: courses where the live enrollment count
// matches the number of grades issued (the section closed cleanly).
return Q().StartAt(N.Course.Type, "CS344")
          .EmitNeighborsSummary();
```

## Reading large lake files

The recipe is optimized for the common case where the data lake file is bigger than process memory but smaller than the table the warehouse generated it from. Three patterns worth knowing:

1. **Column projection beats compression.** Telling Parquet to read only 6 of 50 columns is a 5–10x speedup, not a 5–10% one. Always pass the column list (`GradesIngest.Columns` here).
2. **Row groups are the unit of I/O.** Parquet defaults to ~1 GB row groups for the analytics workload; for streaming ingestion, 100k–500k rows per group is friendlier. Re-write with `pyarrow.parquet.write_table(..., row_group_size=100_000)` if you control the producer.
3. **Schemas evolve.** Avro has explicit reader-vs-writer-schema rules; Parquet has Apache's schema-evolution recommendations. When a column you depend on goes missing, `row.Get<T>(name)` returns `default(T)` — guard at the ingestion layer rather than throwing.

## Parquet vs Avro, briefly

| | Parquet | Avro |
|---|---|---|
| Layout | Columnar | Row-oriented inside blocks |
| Best for | Analytics, projection-heavy reads | Streaming, schema evolution |
| Compression ratio | High (columns compress similarly) | Moderate |
| Schema in file | Yes | Yes |
| Random access | Within row group | Sequential |
| Common producers | Spark, Pandas, Athena, BigQuery export | Kafka, Hadoop, Airflow event logs |

Use Parquet for batch lake files; use Avro when the same schema also drives a Kafka topic (the wire format is shared).

## Reusing this recipe

**Keep as-is**
- `ColumnarSource.cs` — both readers, the `ColumnarRow` shape, and projection are dataset-agnostic.
- `ParquetSample.csproj` — `Parquet.Net`, `Apache.Avro`, `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — node types and edges.
- `GradesIngest.cs`:
  1. `Columns` — the column list you'll actually read.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — `row.Get<T>("col")` per column, then emit nodes + edges.

**Tweak in `Program.cs`**
- The default data path and connector display name.
- The commit cadence (the recipe commits every 1000 rows — drop lower for memory-tight runs).
