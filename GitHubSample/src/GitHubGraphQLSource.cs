using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic GitHub GraphQL reader. The GraphQL pagination
// pattern (search node → pageInfo { endCursor, hasNextPage } + nodes[…])
// is different from the REST recipe's { items, nextCursor } envelope and
// worth teaching on its own — it's how Linear, Shopify, Stripe-V2, and
// every modern API built since ~2018 paginates.
//
// `PagedRequest<T>` runs a query repeatedly, threading `endCursor` into the
// `$after` variable, until `hasNextPage` is false. Honors GitHub's
// `X-RateLimit-Remaining` + `X-RateLimit-Reset` headers (waits when low)
// and the `Retry-After` response on secondary-rate-limit `403`s.
public interface IGitHubGraphQLSource
{
    IAsyncEnumerable<T> PagedRequest<T>(string query, string nodesPath, Dictionary<string, object?>? variables = null, CancellationToken ct = default);
}

public sealed class HttpGitHubGraphQLSource : IGitHubGraphQLSource, IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public HttpGitHubGraphQLSource(string token, string userAgent = "curiosity-recipes-graphql")
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async IAsyncEnumerable<T> PagedRequest<T>(
        string query,
        string nodesPath,
        Dictionary<string, object?>? variables = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        variables ??= new Dictionary<string, object?>();
        string? endCursor = null;

        while (true)
        {
            variables["after"] = endCursor;
            using var response = await PostWithRetryAsync(query, variables, ct);

            var page = await ParsePage<T>(response, nodesPath, ct);
            foreach (var node in page.Nodes) yield return node;
            if (!page.HasNextPage) yield break;
            endCursor = page.EndCursor;
        }
    }

    private async Task<HttpResponseMessage> PostWithRetryAsync(string query, Dictionary<string, object?> variables, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var body = new { query, variables };
            var req  = new HttpRequestMessage(HttpMethod.Post, "graphql")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            var resp = await _http.SendAsync(req, ct);

            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 403)
            {
                await HandleRateLimit(resp, attempt, ct);
                continue;
            }
            if ((int)resp.StatusCode >= 500 && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                continue;
            }

            await WaitIfLowRateLimit(resp, ct);
            resp.EnsureSuccessStatusCode();
            return resp;
        }
        throw new HttpRequestException("GitHub GraphQL: retries exhausted");
    }

    private async Task<PageSlice<T>> ParsePage<T>(HttpResponseMessage resp, string nodesPath, CancellationToken ct)
    {
        var raw = await JsonSerializer.DeserializeAsync<JsonElement>(await resp.Content.ReadAsStreamAsync(ct), Json, ct);
        if (raw.TryGetProperty("errors", out var errs))
            throw new InvalidOperationException("GraphQL errors: " + errs.GetRawText());

        var current = raw.GetProperty("data");
        foreach (var segment in nodesPath.Split('.'))
            current = current.GetProperty(segment);

        var nodes = new List<T>();
        if (current.TryGetProperty("nodes", out var nodesElem))
            foreach (var node in nodesElem.EnumerateArray())
            {
                var typed = node.Deserialize<T>(Json);
                if (typed is not null) nodes.Add(typed);
            }

        var hasNext = false; string? endCursor = null;
        if (current.TryGetProperty("pageInfo", out var pi))
        {
            if (pi.TryGetProperty("hasNextPage", out var hnp)) hasNext = hnp.GetBoolean();
            if (pi.TryGetProperty("endCursor",   out var ec))  endCursor = ec.GetString();
        }
        return new PageSlice<T>(nodes, hasNext, endCursor);
    }

    private static async Task HandleRateLimit(HttpResponseMessage resp, int attempt, CancellationToken ct)
    {
        if (resp.Headers.RetryAfter?.Delta is { } delta) { await Task.Delay(delta, ct); return; }
        var wait = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
        await Task.Delay(wait, ct);
    }

    private static async Task WaitIfLowRateLimit(HttpResponseMessage resp, CancellationToken ct)
    {
        var remaining = ReadIntHeader(resp, "X-RateLimit-Remaining");
        if (remaining is null || remaining > 25) return;

        var reset = ReadLongHeader(resp, "X-RateLimit-Reset");
        if (reset is null) return;

        var resetAt = DateTimeOffset.FromUnixTimeSeconds(reset.Value);
        var delta   = resetAt - DateTimeOffset.UtcNow;
        if (delta > TimeSpan.Zero) await Task.Delay(delta, ct);
    }

    private static int?  ReadIntHeader(HttpResponseMessage resp, string name)
        => resp.Headers.TryGetValues(name, out var values) && int.TryParse(FirstOrNull(values), out var v) ? v : null;

    private static long? ReadLongHeader(HttpResponseMessage resp, string name)
        => resp.Headers.TryGetValues(name, out var values) && long.TryParse(FirstOrNull(values), out var v) ? v : null;

    private static string? FirstOrNull(IEnumerable<string> values)
    {
        foreach (var v in values) return v;
        return null;
    }

    public void Dispose() => _http.Dispose();

    private sealed record PageSlice<T>(List<T> Nodes, bool HasNextPage, string? EndCursor);
}

// Local fallback: each `data/<topic>/page-1.json`, `page-2.json`, ... is a
// raw GraphQL response (`{ "data": { ... } }`) saved off a real run with
// `gh api graphql -f query=...` or curl. The local source threads through
// the same pageInfo logic so ingestion code is identical.
public sealed class LocalGitHubGraphQLSource : IGitHubGraphQLSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _root;

    public LocalGitHubGraphQLSource(string root) { _root = root; }

    public async IAsyncEnumerable<T> PagedRequest<T>(
        string query,
        string nodesPath,
        Dictionary<string, object?>? variables = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var folder = nodesPath.Replace('.', '_');
        var dir    = Path.Combine(_root, folder);
        if (!Directory.Exists(dir)) yield break;

        var files = Directory.GetFiles(dir, "page-*.json");
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var path in files)
        {
            await using var stream = File.OpenRead(path);
            var raw = await JsonSerializer.DeserializeAsync<JsonElement>(stream, Json, ct);
            var current = raw.GetProperty("data");
            foreach (var segment in nodesPath.Split('.'))
                current = current.GetProperty(segment);
            if (!current.TryGetProperty("nodes", out var nodes)) continue;
            foreach (var node in nodes.EnumerateArray())
            {
                var typed = node.Deserialize<T>(Json);
                if (typed is not null) yield return typed;
            }
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GraphQLPagedTopic { Repositories, Issues, PullRequests }
