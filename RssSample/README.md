# RSS / Atom Sample — Polling A Feed With Clean Deduplication

A minimal connector that polls one or more RSS or Atom feeds, dedupes against a persisted set of entry IDs, and writes only the new items into a Curiosity knowledge-graph workspace. Trivially small — but precisely because it's small, it's the cleanest place to show the polling + dedup pattern that drives every long-running connector.

## Code shape

```
RssSample/
├── RssSample.csproj
├── data/
│   ├── feeds/                       ← offline mirror, one feed per file
│   │   ├── mit-news.xml             (RSS 2.0)
│   │   ├── cmu-news.xml             (RSS 2.0)
│   │   ├── stanford-news.atom       (Atom)
│   │   └── berkeley-news.xml        (RSS 2.0)
│   └── .seen                        ← (created at runtime) entry-id dedup set
└── src/
    ├── FeedSource.cs   ← generic: RSS/Atom reader, HTTP + local + SeenEntryStore
    ├── Schema.cs       ← dataset-specific: nodes + edges
    ├── NewsIngest.cs   ← dataset-specific: per-entry ingestion
    └── Program.cs      ← ~60-line glue: poll → dedup → ingest → commit
```

`FeedSource.cs` is the reusable piece. The reader uses `System.ServiceModel.Syndication` so RSS 2.0, RSS 1.0, and Atom all parse with the same code path.

## The data

Four feeds (three RSS, one Atom) covering the same university domain as the rest of the recipes. Each entry has a stable `guid` / `id` that drives the dedup — that's the only piece of state the connector needs to behave correctly across runs.

## The graph

| Source | Node type | Key |
|---|---|---|
| `<item>` / `<entry>` | `NewsItem` | RSS `guid` or Atom `id` |
| Feed root | `Feed` | `feed.id` (or feed URL) |
| `author` | `Author` | author name (shared with S3 sample) |
| `category[*]` | `Category` | `name` |
| heuristic feed→university map | `University` | `name` (shared with CSV/SQL/Sitemap) |

Edges:

```
NewsItem ──PublishedIn──> Feed
NewsItem ──WrittenBy──> Author
NewsItem ──CategorizedAs──> Category
NewsItem ──About──> University
```

## Source abstraction

```csharp
public sealed record FeedEntry(
    string FeedId, string EntryId, string Title, string Summary, string Content,
    string Url, IReadOnlyList<string> Categories,
    string AuthorName, string AuthorEmail,
    DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt);

public interface IFeedSource
{
    IAsyncEnumerable<FeedEntry> ReadAsync(string feedIdOrUrl);
}
```

- `HttpFeedSource` — `HttpClient.GetStringAsync` + `SyndicationFeed.Load`. RSS and Atom both round-trip through `SyndicationFeed` so the consumer never branches on dialect.
- `LocalFeedSource` — same `SyndicationFeed.Load`, but from a file path under `data/feeds/`.
- `SeenEntryStore` — file-backed `HashSet<string>` of entry IDs. `MarkNew(id)` returns `true` once per ID. Persists at the end of the run.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd RssSample

# Local mode — default, four offline feed files:
dotnet run

# Real feeds:
RECIPE_FEED_URLS=https://example.com/feed.xml,https://other.com/atom dotnet run
```

Schedule it on cron — every run picks up only the entries it hasn't seen before.

## Sample queries

```csharp
// News about MIT published this fall.
return Q().StartAt(N.University.Type, "MIT")
          .Out(N.NewsItem.Type, E.NewsAbout)
          .SortByTimestamp(oldestFirst: false)
          .Take(20)
          .Emit("N");
```

```csharp
// Categories used across all feeds (good neighbor-summary smoke test).
return Q().StartAt(N.Category.Type)
          .EmitNeighborsSummary();
```

```csharp
// Combined with the CSV sample: news items mentioning a research area
// that the student's advisor works in (via faculty → research area
// (SQL sample) + news → category, where they share names).
return Q().StartAt(N.Faculty.Type, "h.ortega@cmu.edu")
          .Out(N.ResearchArea.Type, E.Researches)
          .EmitNeighborsSummary();
```

## Polling, in practice

Three operational notes that apply to every feed-based connector, not just this one:

1. **The dedup key is the entry ID, not the URL.** Some feeds change the URL slug after publication (typos, SEO tweaks); the GUID/Atom-ID stays stable. This recipe trusts `item.Id`; falls back to URL when missing.
2. **Update time, not publish time.** When a feed re-emits an existing entry with a corrected typo, you usually *do* want to refresh the graph. To handle that, look up the existing `NewsItem` by ID and compare `UpdatedAt` — only `AddOrUpdate` if the source value is newer. This recipe omits that step for clarity.
3. **Some feeds drop old entries.** A 20-item feed only shows you what's currently on the front page. The connector running daily catches everything; the connector running weekly may miss intermediate items. Match poll cadence to feed retention.

## Dedup vs incremental — what each is for

| | Dedup (this recipe) | Watermark (PostgresSample) |
|---|---|---|
| When to use | Sources that publish a window of recent items, no monotonic key | Sources with an `updated_at` (or sequence) column |
| State persisted | Set of seen IDs | One scalar (the high-watermark) |
| Cost on restart | Has to scan the feed but skips items | Skips immediately to where it left off |
| Failure mode | Slow accumulation of stale IDs (prune by date if it grows) | Clock skew can skip newly-arrived items briefly |

Use dedup when the source has no monotonic ordering you can trust; use the watermark pattern when it does. Some sources warrant both — keep entry-IDs to deduplicate, plus a `since=2024-09-01` query parameter to limit the fetch.

## Reusing this recipe

**Keep as-is**
- `FeedSource.cs` — RSS + Atom parsing, HTTP and local sources, and the dedup store are all dataset-agnostic.
- `RssSample.csproj` — `System.ServiceModel.Syndication` + `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — your domain's nodes and edges.
- `NewsIngest.cs`:
  1. The per-entry mappings — pick which fields become properties vs nodes.
  2. The heuristic that derives a "topic node" from feed metadata.

**Tweak in `Program.cs`**
- The list of `(feedSpec, displayName, topic)` triples baked into local mode.
- The dedup-store location.
- The poll cadence — for a long-running variant, wrap the foreach in a `while (true)` with a delay.
