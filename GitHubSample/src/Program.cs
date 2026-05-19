using System;
using System.Collections.Generic;
using System.IO;
using Curiosity.Library;
using Curiosity.Library.Recipes;
using Microsoft.Extensions.Logging;

var workspaceUrl  = Environment.GetEnvironmentVariable("CURIOSITY_URL")            ?? "http://localhost:8080/";
var apiToken      = Environment.GetEnvironmentVariable("CURIOSITY_API_TOKEN");
var connectorName = Environment.GetEnvironmentVariable("CURIOSITY_CONNECTOR_NAME") ?? "GitHub Sample (Org Repos)";
var ghToken       = Environment.GetEnvironmentVariable("RECIPE_GH_TOKEN");
var ghOrg         = Environment.GetEnvironmentVariable("RECIPE_GH_ORG");
var localRoot     = Environment.GetEnvironmentVariable("RECIPE_LOCAL_ROOT")
                    ?? Path.Combine(AppContext.BaseDirectory, "data");

if (string.IsNullOrWhiteSpace(apiToken))
{
    Console.Error.WriteLine("Missing CURIOSITY_API_TOKEN.");
    return;
}

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger        = loggerFactory.CreateLogger("GitHubSample");

IGitHubGraphQLSource source;
bool live = !string.IsNullOrWhiteSpace(ghToken) && !string.IsNullOrWhiteSpace(ghOrg);
if (live)
{
    source = new HttpGitHubGraphQLSource(ghToken!);
    logger.LogInformation("Reading GitHub GraphQL for org {Org}", ghOrg);
}
else
{
    source = new LocalGitHubGraphQLSource(localRoot);
    logger.LogInformation("Reading from local folder {Root} (set RECIPE_GH_TOKEN + RECIPE_GH_ORG for live)", localRoot);
}

using var graph = Graph.Connect(workspaceUrl, apiToken, connectorName).WithLoggingFactory(loggerFactory);
loggerFactory.AddProvider(graph.GetServerLoggingProvider());

await GitHubIngest.RegisterSchemaAsync(graph);

var repoVars   = new Dictionary<string, object?> { ["owner"] = ghOrg };
var repoCount  = 0;
var repoKeys   = new List<string>();
await foreach (var repo in source.PagedRequest<GitHubIngest.RepoDoc>(
                   GitHubIngest.RepositoriesQuery, "organization.repositories", repoVars))
{
    GitHubIngest.IngestRepo(graph, repo);
    repoKeys.Add(repo.NameWithOwner);
    repoCount++;
}
await graph.CommitPendingAsync();
logger.LogInformation("Ingested {Count} repositories", repoCount);

foreach (var key in repoKeys)
{
    var parts = key.Split('/', 2);
    if (parts.Length != 2) continue;
    var vars = new Dictionary<string, object?> { ["owner"] = parts[0], ["name"] = parts[1] };

    var issues = 0;
    await foreach (var issue in source.PagedRequest<GitHubIngest.IssueDoc>(
                       GitHubIngest.IssuesQuery, "repository.issues", vars))
    {
        GitHubIngest.IngestIssue(graph, key, issue);
        issues++;
    }

    var prs = 0;
    await foreach (var pr in source.PagedRequest<GitHubIngest.PullRequestDoc>(
                       GitHubIngest.PullRequestsQuery, "repository.pullRequests", vars))
    {
        GitHubIngest.IngestPr(graph, key, pr);
        prs++;
    }

    logger.LogInformation("{Repo}: {Issues} issues + {Prs} PRs", key, issues, prs);
    await graph.CommitPendingAsync();
}

(source as IDisposable)?.Dispose();
logger.LogInformation("Done.");
