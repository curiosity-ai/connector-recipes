# GitHub Sample — GraphQL Pagination Over A Naturally Graph-Shaped Source

A minimal connector that walks the GitHub GraphQL API for an organization, ingests its repositories, issues, pull requests, reviews, labels, and topic tags, and turns them into a Curiosity knowledge graph. The shape of the source already *is* a graph (repos own issues, users open PRs, PRs accumulate reviews, topics tag repos); this recipe shows what minimal mapping looks like when you don't have to invent the relationships.

This is the second pagination pattern in the repository — distinct from `RestApiSample`. GraphQL paginates with `{ pageInfo: { endCursor, hasNextPage }, nodes: [...] }` rather than a top-level `{ items, nextCursor }`. The recipe teaches that pattern.

## Code shape

```
GitHubSample/
├── GitHubSample.csproj
├── data/                                ← mock GraphQL responses for offline use
│   ├── organization_repositories/page-1.json
│   ├── repository_issues/page-1.json
│   └── repository_pullRequests/page-1.json
└── src/
    ├── GitHubGraphQLSource.cs ← generic: paginated GraphQL client + local fallback
    ├── Schema.cs              ← dataset-specific: nodes + edges
    ├── GitHubIngest.cs        ← dataset-specific: GraphQL queries + ingestion
    └── Program.cs             ← ~60-line glue: repos → issues + PRs → commit
```

`GitHubGraphQLSource.cs` is the reusable piece. The query strings and the response models in `GitHubIngest.cs` are what you'd rewrite for a different domain on the same API.

## The graph

| Source | Node type | Key |
|---|---|---|
| `Repository.nameWithOwner` | `Repository` | `nameWithOwner` |
| `Repository.owner.login` | `GitHubUser` | `login` |
| `Issue.id` | `Issue` | GraphQL global id |
| `PullRequest.id` | `PullRequest` | GraphQL global id |
| `PullRequest.reviews[*].id` | `Review` | GraphQL global id |
| `Issue.labels[*]` + `PullRequest.labels[*]` | `Label` | `name` |
| `Repository.repositoryTopics[*]` | `Skill` | `name` (shared with CSV/JSON/Mongo!) |

Edges:

```
Repository ──OwnedBy──> GitHubUser
GitHubUser ──OpenedIssue──> Issue ──IssueOf──> Repository
GitHubUser ──OpenedPR──> PullRequest ──PrOf──> Repository
GitHubUser ──ContributedTo──> Repository
Review ──OnPullRequest──> PullRequest
Review ──ReviewedBy──> GitHubUser
Issue / PullRequest ──LabeledWith──> Label
Repository ──CoversTopic──> Skill
```

Topic tags map onto `Skill` deliberately: a repo tagged `python` connects directly to students (CSV) who hold `Python` as a skill, learning resources (JSON) that teach Python, and student projects (Mongo) built with Python.

## Source abstraction

```csharp
public interface IGitHubGraphQLSource
{
    IAsyncEnumerable<T> PagedRequest<T>(
        string query,
        string nodesPath,
        Dictionary<string, object?>? variables = null,
        CancellationToken ct = default);
}
```

- `HttpGitHubGraphQLSource` — `HttpClient` POST to `api.github.com/graphql`, threads `endCursor` into the `$after` variable, parses `data.<path>.pageInfo` and `data.<path>.nodes` on each page. Honors `X-RateLimit-Remaining` + `X-RateLimit-Reset` and the `Retry-After` header on secondary-rate-limit `403`s.
- `LocalGitHubGraphQLSource` — reads `data/<flattened-path>/page-1.json`, `page-2.json`, … as if they were live responses. The flattened path uses `_` for `.` (`organization.repositories` → `organization_repositories/`).

`Program.cs` picks one based on whether `RECIPE_GH_TOKEN` + `RECIPE_GH_ORG` are set.

## Running

### Local mode (default)

```bash
export CURIOSITY_API_TOKEN=<workspace token>
cd GitHubSample
dotnet run
```

### Live mode

Generate a fine-grained personal-access token at https://github.com/settings/tokens (no scopes needed for public repos; for private repos, grant `repo: read`).

```bash
export CURIOSITY_API_TOKEN=<workspace token>
export RECIPE_GH_TOKEN=ghp_xxx
export RECIPE_GH_ORG=curiosity-ai
dotnet run
```

GraphQL costs **one point per page**, not per node, and most queries are well under the 5000-point hourly budget. The recipe waits when the budget drops below 25 so a long run can't crater your account.

## Sample queries

```csharp
// Top contributors across the org.
return Q().StartAt(N.GitHubUser.Type)
          .EmitNeighborsSummary();
```

```csharp
// Repos tagged with knowledge-graph.
return Q().StartAt(N.Skill.Type, "knowledge-graph")
          .Out(N.Repository.Type, E.CoveredByRepo)
          .Emit("N");
```

```csharp
// Combined with CSV/JSON: students whose skills overlap a repo's topics.
return Q().StartAt(N.Student.Type, "S001")
          .Out(N.Skill.Type,      E.HasSkill)
          .Out(N.Repository.Type, E.CoveredByRepo)
          .Emit("N");
```

```csharp
// PRs that ship docs changes.
return Q().StartAt(N.Label.Type, "docs")
          .Out(N.PullRequest.Type, E.LabelOf)
          .Emit("N");
```

## REST vs GraphQL pagination, side by side

| | REST (`RestApiSample`) | GraphQL (this recipe) |
|---|---|---|
| Envelope | `{ items: [...], nextCursor: "..." \| null }` | `{ data: { ..., pageInfo: { endCursor, hasNextPage } } }` |
| Cursor delivery | Query param (`?cursor=…`) | Variable (`$after`) inside the same query |
| Stops on | `nextCursor: null` | `hasNextPage: false` |
| Rate limits | `429` + `Retry-After` | Per-query point cost; `X-RateLimit-Remaining` + `X-RateLimit-Reset` |
| Page size | Server-controlled (often) | Client-controlled per query (`first: 50`) |
| Over-fetching | Common — you take the page as-is | Avoided — you request only the fields you ingest |

The GraphQL pattern shines when the source is naturally graph-shaped (this recipe) and the REST pattern shines when the source is a list of records (the courses recipe).

## Reusing this recipe

**Keep as-is**
- `GitHubGraphQLSource.cs` — the paginated client and local fallback are GitHub-agnostic; they'll work with any GraphQL endpoint that follows the `pageInfo` / `nodes` convention (Shopify, Linear, GitLab, …).
- `GitHubSample.csproj` — `Curiosity.Library` + logging only.

**Replace for your dataset**
- `Schema.cs` — node types and edges.
- `GitHubIngest.cs`:
  1. The GraphQL query strings — pick the fields you care about.
  2. Per-query DTOs (`RepoDoc`, `IssueDoc`, …) — mirror the response shape.
  3. `RegisterSchemaAsync` + the `Ingest*` functions — one per query.

**Tweak in `Program.cs`**
- The `owner`/`name` variables fed to each query.
- The repo-by-repo loop if you need to fan out further (commits per PR, comments per issue, …). The same `PagedRequest<T>` call handles them all.
