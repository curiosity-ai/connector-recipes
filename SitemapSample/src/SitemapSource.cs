using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic sitemap crawler. Workflow:
//   1. Fetch sitemap.xml (handles <sitemapindex> by recursing into child sitemaps).
//   2. Yield each <url><loc> with its lastmod (when present).
//   3. Optional politeness delay between fetches.
//   4. For each URL, fetch the page, extract canonical URL + title + h1 + body
//      text, and hash the content for change detection.
//
// Reuse as-is for any site that publishes a sitemap.xml.
public sealed record SitemapEntry(string Url, DateTimeOffset? LastModified);

public sealed record ScrapedPage(
    string  RequestedUrl,
    string  CanonicalUrl,
    string  Title,
    string  H1,
    string  ContentText,
    string  ContentHash,
    string  Description,
    IReadOnlyList<string> Tags,
    int     StatusCode);

public interface ISitemapSource
{
    IAsyncEnumerable<SitemapEntry> ListUrlsAsync(string sitemapUrlOrPath, CancellationToken ct = default);
    Task<ScrapedPage?>             FetchAsync   (string url, CancellationToken ct = default);
}

public sealed class HttpSitemapSource : ISitemapSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan   _politeness;
    private readonly ILogger?   _logger;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public HttpSitemapSource(TimeSpan politeness, ILogger? logger = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("curiosity-recipes-sitemap/1.0");
        _politeness = politeness;
        _logger     = logger;
    }

    public async IAsyncEnumerable<SitemapEntry> ListUrlsAsync(
        string sitemapUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var xml = await _http.GetStringAsync(sitemapUrl, ct);
        await foreach (var entry in ParseSitemapXml(sitemapUrl, xml, ct)) yield return entry;
    }

    private async IAsyncEnumerable<SitemapEntry> ParseSitemapXml(
        string sourceUrl,
        string xml,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        XDocument doc;
        try   { doc = XDocument.Parse(xml); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Could not parse {Url}", sourceUrl); yield break; }

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        // <sitemapindex> → recurse
        foreach (var index in doc.Descendants(ns + "sitemap"))
        {
            var childUrl = index.Element(ns + "loc")?.Value;
            if (string.IsNullOrWhiteSpace(childUrl)) continue;

            var child = await _http.GetStringAsync(childUrl, ct);
            await foreach (var entry in ParseSitemapXml(childUrl, child, ct)) yield return entry;
        }

        // <urlset> → yield
        foreach (var url in doc.Descendants(ns + "url"))
        {
            var loc = url.Element(ns + "loc")?.Value;
            if (string.IsNullOrWhiteSpace(loc)) continue;

            DateTimeOffset? lastmod = null;
            if (DateTimeOffset.TryParse(url.Element(ns + "lastmod")?.Value, out var dt)) lastmod = dt;
            yield return new SitemapEntry(loc, lastmod);
        }
    }

    public async Task<ScrapedPage?> FetchAsync(string url, CancellationToken ct = default)
    {
        var canonical = Canonicalize(url);
        if (!_seen.Add(canonical)) return null;

        await Task.Delay(_politeness, ct);

        try
        {
            using var resp = await _http.GetAsync(canonical, ct);
            var html = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("HTTP {Status} for {Url}", (int)resp.StatusCode, canonical);
                return new ScrapedPage(url, canonical, string.Empty, string.Empty, string.Empty,
                    Sha256(string.Empty), string.Empty, Array.Empty<string>(), (int)resp.StatusCode);
            }
            return Extract(canonical, html, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fetch failed for {Url}", canonical);
            return null;
        }
    }

    // Lowercases host, strips default ports, removes fragments, normalizes
    // trailing slash on root, drops common tracking parameters. Enough to
    // dedupe "the same page reached two different ways" for most sites; for
    // sites that paginate via path segments or use case-significant query
    // strings, extend the list of dropped params per source.
    public static string Canonicalize(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return raw;

        var builder = new UriBuilder(u)
        {
            Host     = u.Host.ToLowerInvariant(),
            Fragment = string.Empty,
        };
        if ((u.Scheme == "http"  && u.Port == 80) ||
            (u.Scheme == "https" && u.Port == 443))
            builder.Port = -1;

        if (!string.IsNullOrEmpty(u.Query))
        {
            var dropPrefixes = new[] { "utm_", "gclid", "fbclid", "mc_eid", "ref" };
            var kept = u.Query.TrimStart('?').Split('&')
                .Where(kv => !dropPrefixes.Any(p => kv.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            builder.Query = string.Join('&', kept);
        }

        return builder.Uri.ToString();
    }

    private static ScrapedPage Extract(string url, string html, int statusCode)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
        var h1    = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()    ?? string.Empty;
        var desc  = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty) ?? string.Empty;
        var canon = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", string.Empty);
        var canonical = !string.IsNullOrWhiteSpace(canon) ? Canonicalize(canon) : Canonicalize(url);

        // Strip scripts and styles, keep body text.
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//footer") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = (bodyNode?.InnerText ?? string.Empty)
            .Replace("&nbsp;", " ")
            .Replace(' ', ' ');
        bodyText = string.Join(' ', bodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var tags = doc.DocumentNode
            .SelectNodes("//meta[@name='keywords']")
            ?.SelectMany(n => (n.GetAttributeValue("content", "") ?? "").Split(','))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        return new ScrapedPage(
            RequestedUrl: url,
            CanonicalUrl: canonical,
            Title:        title,
            H1:           h1,
            ContentText:  bodyText,
            ContentHash:  Sha256(bodyText),
            Description:  desc,
            Tags:         tags,
            StatusCode:   statusCode);
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();
}

// Local fallback: a single sitemap.xml in `data/` plus an HTML file per <loc>
// under `data/pages/`. Filename per URL is the URL-encoded path. Makes the
// recipe runnable offline and lets tests pin exact HTML.
public sealed class LocalSitemapSource : ISitemapSource
{
    private readonly string _root;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public LocalSitemapSource(string root) { _root = root; }

    public async IAsyncEnumerable<SitemapEntry> ListUrlsAsync(
        string sitemapPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var full = Path.IsPathRooted(sitemapPath) ? sitemapPath : Path.Combine(_root, sitemapPath);
        if (!File.Exists(full)) yield break;

        var xml = await File.ReadAllTextAsync(full, ct);
        XDocument doc;
        try { doc = XDocument.Parse(xml); } catch { yield break; }
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        foreach (var url in doc.Descendants(ns + "url"))
        {
            var loc = url.Element(ns + "loc")?.Value;
            if (string.IsNullOrWhiteSpace(loc)) continue;

            DateTimeOffset? lastmod = null;
            if (DateTimeOffset.TryParse(url.Element(ns + "lastmod")?.Value, out var dt)) lastmod = dt;
            yield return new SitemapEntry(loc, lastmod);
        }
    }

    public async Task<ScrapedPage?> FetchAsync(string url, CancellationToken ct = default)
    {
        var canonical = HttpSitemapSource.Canonicalize(url);
        if (!_seen.Add(canonical)) return null;

        var fileName = UrlToFileName(canonical);
        var full     = Path.Combine(_root, "pages", fileName);
        if (!File.Exists(full)) return null;

        var html = await File.ReadAllTextAsync(full, ct);
        return ExtractStatic(url, canonical, html);
    }

    private static ScrapedPage ExtractStatic(string url, string canonical, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
        var h1    = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()    ?? string.Empty;
        var desc  = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty) ?? string.Empty;

        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//footer") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();
        var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
        var bodyText = string.Join(' ',
            (bodyNode?.InnerText ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var tags = doc.DocumentNode
            .SelectNodes("//meta[@name='keywords']")
            ?.SelectMany(n => (n.GetAttributeValue("content", "") ?? "").Split(','))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList() ?? new List<string>();

        var sha = SHA256.HashData(Encoding.UTF8.GetBytes(bodyText));
        var sb  = new StringBuilder(sha.Length * 2);
        foreach (var b in sha) sb.Append(b.ToString("x2"));

        return new ScrapedPage(url, canonical, title, h1, bodyText, sb.ToString(), desc, tags, 200);
    }

    public static string UrlToFileName(string url)
    {
        var uri      = new Uri(url);
        var path     = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(path)) path = "index";
        var host     = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        var fileSafe = (host + "_" + path).Replace('/', '_').Replace('.', '_');
        return fileSafe + ".html";
    }
}
