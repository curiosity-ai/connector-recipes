# Kafka Sample — Near-Realtime CDC Stream Into The Graph

A minimal connector that consumes a Kafka topic of change-data-capture (CDC) events and turns each event into an idempotent graph mutation. Closes the loop on "how do I keep the graph fresh" by pushing the model from batch reruns to a continuous stream. Includes a local JSONL fallback so the recipe works without a Kafka cluster.

## Code shape

```
KafkaSample/
├── KafkaSample.csproj
├── data/
│   ├── events.jsonl   ← 12 enrollment events (offline replay)
│   └── .offset        ← (created at runtime) last offset committed locally
└── src/
    ├── EventStreamSource.cs  ← generic: Kafka + JSONL implementations
    ├── Schema.cs             ← dataset-specific: nodes + edges
    ├── EnrollmentsIngest.cs  ← dataset-specific: event model + ingestion
    └── Program.cs            ← ~60-line glue: consume → ingest → commit offset
```

`EventStreamSource.cs` is the reusable piece — both implementations expose the same `IAsyncEnumerable<StreamEvent<T>>` plus a `CommitOffset` that signals durable processing.

## The data

Twelve enrollment events spanning the Fall 2024 / Spring 2025 add-drop window. Same student appears multiple times: `S001/MATH210/Fall 2024` starts as `Waitlisted`, transitions to `Enrolled` six days later. `S004/CS210/Fall 2024` enrolls and then drops. `S001/CS344/Fall 2024` enrolls in August and lands a grade in December. Same `Enrollment` node, three property updates over four months.

```jsonc
// One event line in events.jsonl
{
  "key":   "S001/CS344/Fall 2024",
  "value": {
    "eventType": "enrolled",
    "studentId": "S001",
    "courseCode": "CS344",
    "term":       "Fall 2024",
    "status":     "Enrolled",
    "grade":      "",
    "occurredAt": "2024-08-21T09:14:00Z"
  }
}
```

The key (`studentId/courseCode/term`) is the deterministic composite key of the `Enrollment` node — that's what makes the ingest idempotent.

## The graph

| Source field | Node type | Key |
|---|---|---|
| `studentId` | `Student` | `studentId` (shared with CSV/Mongo/Parquet/REST) |
| `courseCode` | `Course` | `code` (shared with REST API) |
| `term` | `Term` | `name` (shared with REST API) |
| composite of all three | `Enrollment` | `"<student>/<course>/<term>"` |

Edges:

```
Student ──HasEnrollment──> Enrollment ──ForCourse──> Course
                                ──DuringTerm──> Term
```

The `Enrollment` node carries the *mutable* state (status, grade, updated_at). The `Student → Enrollment → Course` triple is the *immutable* identity — both are derived from the same composite key.

## Source abstraction

```csharp
public sealed record StreamEvent<T>(string Key, T Value, long Offset, int Partition);

public interface IEventStreamSource<T>
{
    IAsyncEnumerable<StreamEvent<T>> ConsumeAsync(CancellationToken ct = default);
    void CommitOffset(StreamEvent<T> evt);
    void Dispose();
}
```

- `KafkaEventStreamSource<T>` — `Confluent.Kafka` consumer with **manual** offset commit. Offsets only advance after the graph commit succeeds, so a crash mid-batch re-delivers the unfinished messages on the next run.
- `JsonlEventStreamSource<T>` — reads one JSON document per line; saves the current line number to `data/.offset`. Same semantics: on restart, resumes from the last committed offset.

## Running

### Local mode (default)

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd KafkaSample
dotnet run
```

Re-run: the offset file lets the second run skip events processed by the first. Delete `data/.offset` to replay from the top.

### Real Kafka

```bash
export CURIOSITY_API_TOKEN=<workspace token>
export RECIPE_KAFKA_BOOTSTRAP=localhost:9092
export RECIPE_KAFKA_TOPIC=enrollments
export RECIPE_KAFKA_GROUP=curiosity-enrollments
dotnet run
```

The recipe stops after `RECIPE_MAX_IDLE_SECONDS` (default 5) without new events — useful for cron-style runs. Set the env var to `0` to run forever and let a supervisor restart it.

A minimal local Kafka for testing:

```bash
docker run -d --name kafka -p 9092:9092 \
  -e KAFKA_CFG_NODE_ID=0 \
  -e KAFKA_CFG_PROCESS_ROLES=controller,broker \
  -e KAFKA_CFG_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093 \
  -e KAFKA_CFG_ADVERTISED_LISTENERS=PLAINTEXT://localhost:9092 \
  -e KAFKA_CFG_CONTROLLER_QUORUM_VOTERS=0@kafka:9093 \
  -e KAFKA_CFG_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_CFG_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  bitnami/kafka:latest
```

Then produce one of the demo events:

```bash
docker exec -i kafka kafka-console-producer.sh \
  --bootstrap-server localhost:9092 --topic enrollments < data/events.jsonl
```

## Sample queries

```csharp
// S001's current enrollment status across all courses.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Enrollment.Type, E.HasEnrollment)
          .Emit("N");
```

```csharp
// Students who completed CS210 last fall, with grade.
return Q().StartAt(N.Course.Type, "CS210")
          .Out(N.Enrollment.Type, E.CourseOf)
          .Where(e => e.GetString(N.Enrollment.Status) == "Completed")
          .Emit("N");
```

```csharp
// Combined with the REST API sample: courses + their current enrollments +
// the instructors teaching them — the live state of a section.
return Q().StartAt(N.Course.Type, "CS344")
          .Out(N.Enrollment.Type, E.CourseOf)
          .Out(N.Student.Type,    E.EnrolledIn)
          .Emit("N");
```

## Why this is "exactly-once-ish"

The recipe gives you at-least-once delivery (no message loss on crash) with idempotent application (no duplicate effects on replay). Together these approximate exactly-once semantics for ingestion, without needing transactional support from Kafka.

Three things make the idempotency work:

1. **Deterministic composite keys.** `EnrollmentsIngest.ComposeKey(event)` returns the same string for the same input. Re-processing the event lands on the same `Enrollment` node.
2. **`AddOrUpdate` not `TryAdd`.** Property values are overwritten with each event, so the graph converges to the last-write-wins state regardless of replay history.
3. **Commit graph first, then offset.** `CommitPendingAsync()` runs before `CommitOffset()`. If the process dies between them, the next run reprocesses the last batch — which is fine because of (1) and (2).

The corner cases this *doesn't* handle (a real CDC source would):

- **Out-of-order events.** If the stream guarantees per-key ordering (Kafka does, within a partition, when keyed by the same field used for the composite key here), this is moot. Otherwise add an `if (evt.OccurredAt < existing.UpdatedAt) skip`.
- **Hard deletes.** Real CDC streams emit tombstones (`{ key: "...", value: null }`). The downstream effect is to *remove* the node, not upsert it. This recipe doesn't ship that path — you'd add an `if (envelope.Value is null) graph.Remove(...)` branch.

## Reusing this recipe

**Keep as-is**
- `EventStreamSource.cs` — Kafka client + JSONL fallback + offset commit are dataset-agnostic.
- `Program.cs` — the consume / ingest / commit loop.
- `KafkaSample.csproj` — `Confluent.Kafka` + `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — your domain's nodes and edges.
- `EnrollmentsIngest.cs`:
  1. The `EnrollmentEvent` class — mirror the value shape of one message.
  2. `RegisterSchemaAsync` — list every node type + `CreateEdgeSchemaAsync(typeof(Edges))`.
  3. `Ingest` — `AddOrUpdate` on the keyed primary node, `TryAdd` + `Link` on related ones.
  4. `ComposeKey` — make it deterministic from the event payload.

**Tweak in `Program.cs`**
- The default topic / group / batch size.
- The idle-timeout shutdown for cron-style runs.
- Tombstone handling, if your stream uses them.
