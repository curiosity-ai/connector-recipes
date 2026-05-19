using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: GraphQL response models, schema registration, and the
// ingestion that wires up repos → users → issues → PRs → reviews → labels →
// topics. The GraphQL queries themselves live as string constants below;
// you'd write a separate set of queries for whatever GitHub objects you
// actually care about.
public static class GitHubIngest
{
    public const string RepositoriesQuery = @"
query($owner: String!, $after: String) {
  organization(login: $owner) {
    repositories(first: 25, after: $after) {
      pageInfo { hasNextPage endCursor }
      nodes {
        nameWithOwner
        description
        url
        primaryLanguage { name }
        stargazerCount
        forkCount
        isArchived
        createdAt
        owner { login }
        repositoryTopics(first: 20) { nodes { topic { name } } }
      }
    }
  }
}";

    public const string IssuesQuery = @"
query($owner: String!, $name: String!, $after: String) {
  repository(owner: $owner, name: $name) {
    issues(first: 50, after: $after, orderBy: { field: CREATED_AT, direction: DESC }) {
      pageInfo { hasNextPage endCursor }
      nodes {
        id number title state url createdAt
        author { login }
        labels(first: 20) { nodes { name } }
      }
    }
  }
}";

    public const string PullRequestsQuery = @"
query($owner: String!, $name: String!, $after: String) {
  repository(owner: $owner, name: $name) {
    pullRequests(first: 50, after: $after, orderBy: { field: CREATED_AT, direction: DESC }) {
      pageInfo { hasNextPage endCursor }
      nodes {
        id number title state url createdAt merged
        author { login }
        reviews(first: 10) { nodes { id state author { login } } }
        labels(first: 20) { nodes { name } }
      }
    }
  }
}";

    public sealed class RepoDoc
    {
        [JsonPropertyName("nameWithOwner")]   public string         NameWithOwner   { get; set; } = string.Empty;
        [JsonPropertyName("description")]     public string?        Description     { get; set; }
        [JsonPropertyName("url")]             public string         Url             { get; set; } = string.Empty;
        [JsonPropertyName("primaryLanguage")] public LangRef?       PrimaryLanguage { get; set; }
        [JsonPropertyName("stargazerCount")]  public int            StargazerCount  { get; set; }
        [JsonPropertyName("forkCount")]       public int            ForkCount       { get; set; }
        [JsonPropertyName("isArchived")]      public bool           IsArchived      { get; set; }
        [JsonPropertyName("createdAt")]       public DateTimeOffset CreatedAt       { get; set; }
        [JsonPropertyName("owner")]           public UserRef?       Owner           { get; set; }
        [JsonPropertyName("repositoryTopics")] public TopicsConnection? RepositoryTopics { get; set; }
    }

    public sealed class IssueDoc
    {
        [JsonPropertyName("id")]        public string         Id        { get; set; } = string.Empty;
        [JsonPropertyName("number")]    public int            Number    { get; set; }
        [JsonPropertyName("title")]     public string         Title     { get; set; } = string.Empty;
        [JsonPropertyName("state")]     public string         State     { get; set; } = string.Empty;
        [JsonPropertyName("url")]       public string         Url       { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("author")]    public UserRef?       Author    { get; set; }
        [JsonPropertyName("labels")]    public LabelsConnection? Labels { get; set; }
    }

    public sealed class PullRequestDoc
    {
        [JsonPropertyName("id")]        public string         Id        { get; set; } = string.Empty;
        [JsonPropertyName("number")]    public int            Number    { get; set; }
        [JsonPropertyName("title")]     public string         Title     { get; set; } = string.Empty;
        [JsonPropertyName("state")]     public string         State     { get; set; } = string.Empty;
        [JsonPropertyName("url")]       public string         Url       { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("merged")]    public bool           Merged    { get; set; }
        [JsonPropertyName("author")]    public UserRef?       Author    { get; set; }
        [JsonPropertyName("reviews")]   public ReviewsConnection? Reviews { get; set; }
        [JsonPropertyName("labels")]    public LabelsConnection?  Labels  { get; set; }
    }

    public sealed class ReviewDoc
    {
        [JsonPropertyName("id")]     public string   Id     { get; set; } = string.Empty;
        [JsonPropertyName("state")]  public string   State  { get; set; } = string.Empty;
        [JsonPropertyName("author")] public UserRef? Author { get; set; }
    }

    public sealed class LangRef    { [JsonPropertyName("name")]  public string  Name  { get; set; } = string.Empty; }
    public sealed class UserRef    { [JsonPropertyName("login")] public string? Login { get; set; } }
    public sealed class TopicRef   { [JsonPropertyName("topic")] public NameRef? Topic { get; set; } }
    public sealed class NameRef    { [JsonPropertyName("name")]  public string  Name  { get; set; } = string.Empty; }
    public sealed class LabelsConnection  { [JsonPropertyName("nodes")] public List<NameRef>   Nodes { get; set; } = new(); }
    public sealed class TopicsConnection  { [JsonPropertyName("nodes")] public List<TopicRef>  Nodes { get; set; } = new(); }
    public sealed class ReviewsConnection { [JsonPropertyName("nodes")] public List<ReviewDoc> Nodes { get; set; } = new(); }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Repository>();
        await graph.CreateNodeSchemaAsync<Nodes.GitHubUser>();
        await graph.CreateNodeSchemaAsync<Nodes.Issue>();
        await graph.CreateNodeSchemaAsync<Nodes.PullRequest>();
        await graph.CreateNodeSchemaAsync<Nodes.Review>();
        await graph.CreateNodeSchemaAsync<Nodes.Label>();
        await graph.CreateNodeSchemaAsync<Nodes.Skill>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void IngestRepo(Graph graph, RepoDoc doc)
    {
        var repo = graph.AddOrUpdate(new Nodes.Repository
        {
            NameWithOwner   = doc.NameWithOwner,
            Description     = doc.Description ?? string.Empty,
            Url             = doc.Url,
            PrimaryLanguage = doc.PrimaryLanguage?.Name ?? string.Empty,
            Stars           = doc.StargazerCount,
            Forks           = doc.ForkCount,
            IsArchived      = doc.IsArchived,
            CreatedAt       = doc.CreatedAt,
        });

        if (!string.IsNullOrWhiteSpace(doc.Owner?.Login))
        {
            var owner = graph.TryAdd(new Nodes.GitHubUser { Login = doc.Owner.Login! });
            graph.Link(repo, owner, Edges.OwnedBy, Edges.Owns);
        }

        foreach (var topic in doc.RepositoryTopics?.Nodes ?? new())
        {
            var name = topic.Topic?.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var skill = graph.TryAdd(new Nodes.Skill { Name = name });
            graph.Link(repo, skill, Edges.CoversTopic, Edges.CoveredByRepo);
        }
    }

    public static void IngestIssue(Graph graph, string repoKey, IssueDoc doc)
    {
        var issue = graph.AddOrUpdate(new Nodes.Issue
        {
            Id        = doc.Id,
            Number    = doc.Number,
            Title     = doc.Title,
            State     = doc.State,
            Url       = doc.Url,
            CreatedAt = doc.CreatedAt,
        });

        graph.Link(issue, Node.FromKey(nameof(Nodes.Repository), repoKey), Edges.IssueOf, Edges.HasIssue);

        if (!string.IsNullOrWhiteSpace(doc.Author?.Login))
        {
            var user = graph.TryAdd(new Nodes.GitHubUser { Login = doc.Author.Login! });
            graph.Link(user, issue, Edges.OpenedIssue, Edges.OpenedBy);
        }

        foreach (var label in doc.Labels?.Nodes ?? new())
        {
            if (string.IsNullOrWhiteSpace(label.Name)) continue;
            var node = graph.TryAdd(new Nodes.Label { Name = label.Name });
            graph.Link(issue, node, Edges.LabeledWith, Edges.LabelOf);
        }
    }

    public static void IngestPr(Graph graph, string repoKey, PullRequestDoc doc)
    {
        var pr = graph.AddOrUpdate(new Nodes.PullRequest
        {
            Id        = doc.Id,
            Number    = doc.Number,
            Title     = doc.Title,
            State     = doc.State,
            Url       = doc.Url,
            Merged    = doc.Merged,
            CreatedAt = doc.CreatedAt,
        });

        graph.Link(pr, Node.FromKey(nameof(Nodes.Repository), repoKey), Edges.PrOf, Edges.HasPullRequest);

        if (!string.IsNullOrWhiteSpace(doc.Author?.Login))
        {
            var user = graph.TryAdd(new Nodes.GitHubUser { Login = doc.Author.Login! });
            graph.Link(user, pr, Edges.OpenedPR, Edges.OpenedPRBy);
            graph.Link(user, Node.FromKey(nameof(Nodes.Repository), repoKey), Edges.ContributedTo, Edges.Contributor);
        }

        foreach (var label in doc.Labels?.Nodes ?? new())
        {
            if (string.IsNullOrWhiteSpace(label.Name)) continue;
            var node = graph.TryAdd(new Nodes.Label { Name = label.Name });
            graph.Link(pr, node, Edges.LabeledWith, Edges.LabelOf);
        }

        foreach (var review in doc.Reviews?.Nodes ?? new())
        {
            var reviewNode = graph.AddOrUpdate(new Nodes.Review
            {
                Id    = review.Id,
                State = review.State,
            });
            graph.Link(reviewNode, pr, Edges.OnPullRequest, Edges.HasReview);

            if (!string.IsNullOrWhiteSpace(review.Author?.Login))
            {
                var user = graph.TryAdd(new Nodes.GitHubUser { Login = review.Author.Login! });
                graph.Link(reviewNode, user, Edges.ReviewedBy, Edges.Reviewed);
            }
        }
    }
}
