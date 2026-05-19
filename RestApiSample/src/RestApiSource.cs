using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic REST API reader. Handles bearer-token auth,
// cursor-based pagination, 429 rate-limiting (honors Retry-After), and
// exponential backoff for transient errors. Reuse as-is for any paginated
// REST source — only the response shape and ingestion code change.
//
// The page contract is intentionally minimal:
//   { "items": [ ... ], "nextCursor": "abc" | null }
// Most SaaS APIs return some variant of this; adapt PageResponse<T> if
// your endpoint uses link headers, offset paging, or a different field name.
public interface IRestApiSource
{
    IAsyncEnumerable<T> StreamAsync<T>(string path, CancellationToken ct = default);
}

public sealed class PageResponse<T>
{
    public List<T> Items      { get; set; } = new();
    public string? NextCursor { get; set; }
}

public sealed class HttpRestApiSource : IRestApiSource, IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string     _cursorParam;
    private readonly int        _maxAttempts;

    public HttpRestApiSource(string baseUrl, string? bearerToken, string cursorParam = "cursor", int maxAttempts = 5)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/") };
        if (!string.IsNullOrWhiteSpace(bearerToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _cursorParam  = cursorParam;
        _maxAttempts  = maxAttempts;
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? cursor = null;
        do
        {
            var url  = cursor is null ? path : $"{path}{(path.Contains('?') ? '&' : '?')}{_cursorParam}={Uri.EscapeDataString(cursor)}";
            var page = await GetWithRetryAsync<PageResponse<T>>(url, ct);
            if (page is null) yield break;

            foreach (var item in page.Items) yield return item;
            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    private async Task<TResp?> GetWithRetryAsync<TResp>(string url, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct);

                if (resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500)
                {
                    var delay = ComputeBackoff(resp, attempt);
                    if (attempt == _maxAttempts) resp.EnsureSuccessStatusCode();
                    await Task.Delay(delay, ct);
                    continue;
                }

                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                return await JsonSerializer.DeserializeAsync<TResp>(stream, Json, ct);
            }
            catch (HttpRequestException) when (attempt < _maxAttempts)
            {
                await Task.Delay(ComputeBackoff(null, attempt), ct);
            }
        }
        return default;
    }

    private static TimeSpan ComputeBackoff(HttpResponseMessage? resp, int attempt)
    {
        if (resp?.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (resp?.Headers.RetryAfter?.Date  is { } date)  return date - DateTimeOffset.UtcNow;
        var seconds = Math.Min(60, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds);
    }

    public void Dispose() => _http.Dispose();
}

// Drop-in local fallback for offline development. Each "page" is one file
// under data/pages/, named page-1.json, page-2.json, ... — the nextCursor
// field in each file names the next file.
public sealed class LocalRestApiSource : IRestApiSource
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _root;

    public LocalRestApiSource(string root) { _root = root; }

    public async IAsyncEnumerable<T> StreamAsync<T>(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var file = "page-1.json";
        while (!string.IsNullOrEmpty(file))
        {
            var full = Path.Combine(_root, path, file);
            if (!File.Exists(full)) yield break;

            await using var stream = File.OpenRead(full);
            var page = await JsonSerializer.DeserializeAsync<PageResponse<T>>(stream, Json, ct);
            if (page is null) yield break;

            foreach (var item in page.Items) yield return item;
            file = page.NextCursor;
        }
    }
}
