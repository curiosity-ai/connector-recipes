using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic RSS / Atom reader. `SyndicationFeed.Load`
// handles both dialects out of the box, so the same code path works against
// RSS 2.0, RSS 1.0, and Atom feeds.
//
// Reuse as-is for any polling-feed source. The recipe demonstrates dedup
// against a persisted ID set, which is the only piece of state a feed
// connector needs to behave correctly across runs.
public sealed record FeedEntry(
    string                FeedId,
    string                EntryId,
    string                Title,
    string                Summary,
    string                Content,
    string                Url,
    IReadOnlyList<string> Categories,
    string                AuthorName,
    string                AuthorEmail,
    DateTimeOffset        PublishedAt,
    DateTimeOffset        UpdatedAt);

public interface IFeedSource
{
    IAsyncEnumerable<FeedEntry> ReadAsync(string feedIdOrUrl);
}

public sealed class HttpFeedSource : IFeedSource, IDisposable
{
    private readonly HttpClient _http;

    public HttpFeedSource(string userAgent = "curiosity-recipes-feeds/1.0")
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async IAsyncEnumerable<FeedEntry> ReadAsync(string feedUrl)
    {
        var xml = await _http.GetStringAsync(feedUrl);
        using var reader = XmlReader.Create(new StringReader(xml));
        var feed = SyndicationFeed.Load(reader);
        if (feed is null) yield break;

        var feedId = feed.Id;
        if (string.IsNullOrWhiteSpace(feedId)) feedId = feedUrl;

        foreach (var entry in Enumerate(feed, feedId)) yield return entry;
    }

    public void Dispose() => _http.Dispose();

    public static IEnumerable<FeedEntry> Enumerate(SyndicationFeed feed, string feedId)
    {
        foreach (var item in feed.Items)
        {
            var url        = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
            var entryId    = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : url;
            var summary    = item.Summary?.Text ?? string.Empty;
            var content    = (item.Content as TextSyndicationContent)?.Text ?? summary;
            var author     = item.Authors.FirstOrDefault();
            var categories = item.Categories.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

            yield return new FeedEntry(
                FeedId:      feedId,
                EntryId:     entryId,
                Title:       item.Title?.Text ?? string.Empty,
                Summary:     summary,
                Content:     content,
                Url:         url,
                Categories:  categories,
                AuthorName:  author?.Name  ?? string.Empty,
                AuthorEmail: author?.Email ?? string.Empty,
                PublishedAt: item.PublishDate,
                UpdatedAt:   item.LastUpdatedTime);
        }
    }
}

// Local fallback: each `*.xml` under data/feeds/ is one feed file (RSS or
// Atom). Filename serves as the feed ID. Makes the recipe runnable offline
// and lets tests pin exact entries.
public sealed class LocalFeedSource : IFeedSource
{
    private readonly string _root;

    public LocalFeedSource(string root) { _root = root; }

#pragma warning disable CS1998 // sync XmlReader in an async iterator; intentional
    public async IAsyncEnumerable<FeedEntry> ReadAsync(string feedId)
    {
        var path = Path.IsPathRooted(feedId) ? feedId : Path.Combine(_root, feedId);
        if (!File.Exists(path)) yield break;

        using var reader = XmlReader.Create(path);
        var feed = SyndicationFeed.Load(reader);
        if (feed is null) yield break;

        var idForFeed = !string.IsNullOrWhiteSpace(feed.Id)
            ? feed.Id
            : Path.GetFileNameWithoutExtension(path);

        foreach (var entry in HttpFeedSource.Enumerate(feed, idForFeed)) yield return entry;
    }
#pragma warning restore CS1998

    public IEnumerable<string> ListFeedFiles() =>
        Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "*.xml")
            : Array.Empty<string>();
}

// Simple file-backed dedup store. One line per seen entry ID. Drop-in
// replacement: persist to a graph metadata node, Redis, or a SQL table —
// the contract is just Add(id) → bool (was new) and Save().
public sealed class SeenEntryStore
{
    private readonly string         _path;
    private readonly HashSet<string> _ids;

    public SeenEntryStore(string path)
    {
        _path = path;
        _ids  = File.Exists(path)
            ? new HashSet<string>(File.ReadAllLines(path))
            : new HashSet<string>();
    }

    public bool MarkNew(string id) => _ids.Add(id);
    public int  Count               => _ids.Count;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllLines(_path, _ids);
    }
}
