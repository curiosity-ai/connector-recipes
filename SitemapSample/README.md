# Sitemap Sample — Web Scraping With Canonicalization And Change Detection

A minimal connector that walks a site's `sitemap.xml`, fetches each URL with a polite delay between requests, extracts the page's title / heading / body text / meta tags, deduplicates by **canonicalized URL**, hashes content for change detection, and ingests the result into a Curiosity knowledge-graph workspace. The pattern for taming a messy source: dedup, error recovery, partial success.

## Code shape

```
SitemapSample/
├── SitemapSample.csproj
├── data/                                  ← offline mirror for tests / dev
│   ├── sitemap.xml
│   └── pages/<host>_<path>.html           ← one file per URL
└── src/
    ├── SitemapSource.cs ← generic: ISitemapSource + Http + Local implementations
    ├── Schema.cs        ← dataset-specific: nodes + edges
    ├── WebsiteIngest.cs ← dataset-specific: per-page ingestion
    └── Program.cs       ← ~50-line glue: list → fetch (dedup) → ingest → commit
```

`SitemapSource.cs` is the reusable piece. `Schema.cs` and `WebsiteIngest.cs` are what you rewrite for your own site.

## What it solves

Scraping a sitemap looks like a four-line script (fetch sitemap, foreach url, fetch html, extract text) — until it isn't. The recipe addresses the predictable ways that script fails on real sites:

| Failure mode | Recipe response |
|---|---|
| Same page reachable two ways (with/without `www`, trailing slash, `?utm_source=…`) | `Canonicalize(url)`: lowercase host, drop default port, strip fragment + tracking params, normalize to single canonical URL. |
| Sitemap index file points at child sitemaps | `<sitemapindex>` is detected and recursively expanded. |
| Some URLs are 404 / 500 / network-error | Each `FetchAsync` is wrapped — on failure, log and move on, never stop the run. Partial success is success. |
| Same canonical URL appears in two sitemap entries | `HashSet<string>` of seen canonicals; second call returns `null`. |
| Content hasn't changed since last run | `ContentHash` (SHA-256 of body text) on every `WebPage` node; downstream consumers can skip unchanged pages. |
| Site asks crawlers to slow down | `RECIPE_POLITENESS_MS` delay between requests (default 500 ms). |
| `<link rel="canonical">` overrides the requested URL | Recipe reads it and stores the link-tag's canonical, not the requested URL. |

## The graph

| Source | Node type | Key |
|---|---|---|
| One scraped URL | `WebPage` | canonical URL |
| Page host | `Website` | host (e.g. `mit.edu`) |
| Heuristic host → university map | `University` | `name` (shared with CSV/SQL) |
| `meta name="keywords"` | `Tag` | `name` |
| First path segment | `Section` | `"<host>/<segment>"` |

Edges:

```
WebPage ──HostedOn──> Website
WebPage ──AboutUniversity──> University   (when host matches mit.edu/cmu.edu/…)
WebPage ──TaggedWith──> Tag
WebPage ──InSection──> Section
```

## Source abstraction

```csharp
public interface ISitemapSource
{
    IAsyncEnumerable<SitemapEntry> ListUrlsAsync(string sitemapUrlOrPath, CancellationToken ct = default);
    Task<ScrapedPage?>             FetchAsync   (string url,             CancellationToken ct = default);
}
```

- `HttpSitemapSource` — fetches `sitemap.xml` (and any nested `<sitemap>` entries), then `HttpClient.GetAsync` per URL with a configurable politeness delay. Canonicalizes every URL, dedupes, parses HTML with `HtmlAgilityPack`.
- `LocalSitemapSource` — same XML reader, but `FetchAsync` reads from `data/pages/<host>_<path>.html` instead. The filename derivation is exposed as `LocalSitemapSource.UrlToFileName(url)` so you can drop in new fixtures predictably.

Ingestion code is identical in both modes.

## Running

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd SitemapSample

# Local mode — default, works out of the box:
dotnet run

# Crawl a real site:
RECIPE_SITEMAP_URL=https://example.com/sitemap.xml \
RECIPE_POLITENESS_MS=1000 \
dotnet run
```

The included sitemap has **eight `<url>` entries**: the recipe will yield seven unique pages — one of the entries is the same MIT About page with UTM tracking parameters, which the canonicalizer collapses back to the original.

## Sample queries

```csharp
// All pages under MIT's research section.
return Q().StartAt(N.Section.Type, "mit.edu/research")
          .Out(N.WebPage.Type, E.Contains)
          .Emit("N");
```

```csharp
// Pages tagged with both "Computer Science" and "Pittsburgh" (CMU's CS page).
return Q().StartAt(N.Tag.Type, "Computer Science")
          .Out(N.WebPage.Type, E.TagOf)
          .Where(p => p.GetString(N.WebPage.Content).Contains("Pittsburgh"))
          .Emit("N");
```

```csharp
// Combined with the CSV sample: students at MIT plus the public web pages
// about MIT — useful for grounding LLM responses against the same university.
return Q().StartAt(N.University.Type, "MIT")
          .EmitNeighborsSummary();
```

## Polite crawling, in practice

Three things to do before pointing this at a site you don't own:

1. **Read robots.txt.** If the site disallows your user-agent under `/`, stop. This recipe doesn't parse robots.txt — add it if you're going to crawl outside your own domain.
2. **Set a meaningful `User-Agent`.** The recipe sets `curiosity-recipes-sitemap/1.0` — make yours specific to your project plus an email or URL the site owner can reach you at if you cause problems.
3. **Rate-limit by domain, not by site.** A small static site is happy with 200 ms between requests; a large news site might publish a `Crawl-delay` directive of 10 seconds. The recipe's `RECIPE_POLITENESS_MS` is a single number — for multi-domain crawls, parameterize per host.

## Reusing this recipe

**Keep as-is**
- `SitemapSource.cs` — all the messy-source plumbing (canonicalization, dedup, fault tolerance, content hash) is dataset-agnostic.
- `SitemapSample.csproj` — `HtmlAgilityPack` + `Curiosity.Library`.

**Replace for your dataset**
- `Schema.cs` — pick the entities you care about. Most sites need `WebPage`, `Website`, and a domain-specific category node ("Product", "Article", "Person", …).
- `WebsiteIngest.cs`:
  1. The host → entity mapping (the `MapHostToUniversity` switch here).
  2. The section extraction (one path segment is often the wrong granularity).
  3. Additional extraction passes (Open Graph, JSON-LD, structured data) before linking.

**Tweak in `Program.cs`**
- The connector display name.
- Default politeness.
- Per-page commit cadence (the recipe commits every 25 pages — drop lower for very expensive sites you don't want to crawl twice).
