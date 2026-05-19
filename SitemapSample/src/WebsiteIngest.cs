using System;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: schema registration and the ingestion that builds the
// WebPage / Website / Section / Tag graph from a scraped page. The university
// mapping is heuristic — for production, replace with a config table.
public static class WebsiteIngest
{
    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.WebPage>();
        await graph.CreateNodeSchemaAsync<Nodes.Website>();
        await graph.CreateNodeSchemaAsync<Nodes.University>();
        await graph.CreateNodeSchemaAsync<Nodes.Tag>();
        await graph.CreateNodeSchemaAsync<Nodes.Section>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, ScrapedPage page, DateTimeOffset? lastModified)
    {
        var uri = new Uri(page.CanonicalUrl);

        var webPage = graph.AddOrUpdate(new Nodes.WebPage
        {
            Url          = page.CanonicalUrl,
            Title        = page.Title,
            H1           = page.H1,
            Description  = page.Description,
            Content      = page.ContentText,
            ContentHash  = page.ContentHash,
            StatusCode   = page.StatusCode,
            LastModified = lastModified ?? DateTimeOffset.UtcNow,
        });

        var site = graph.AddOrUpdate(new Nodes.Website
        {
            Host = uri.Host,
            Name = MapHostToName(uri.Host),
        });
        graph.Link(webPage, site, Edges.HostedOn, Edges.Hosts);

        var universityName = MapHostToUniversity(uri.Host);
        if (universityName is not null)
        {
            var university = graph.TryAdd(new Nodes.University { Name = universityName });
            graph.Link(webPage, university, Edges.AboutUniversity, Edges.WebPagesAbout);
        }

        var sectionPath = ExtractSection(uri.AbsolutePath);
        if (!string.IsNullOrEmpty(sectionPath))
        {
            var section = graph.AddOrUpdate(new Nodes.Section
            {
                Path = $"{uri.Host}{sectionPath}",
                Name = sectionPath.TrimStart('/'),
            });
            graph.Link(webPage, section, Edges.InSection, Edges.Contains);
        }

        foreach (var tagName in page.Tags)
        {
            if (string.IsNullOrWhiteSpace(tagName)) continue;
            var tag = graph.TryAdd(new Nodes.Tag { Name = tagName });
            graph.Link(webPage, tag, Edges.TaggedWith, Edges.TagOf);
        }
    }

    private static string MapHostToName(string host) =>
        host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);

    private static string? MapHostToUniversity(string host) => host.ToLowerInvariant() switch
    {
        var h when h.EndsWith("mit.edu")        => "MIT",
        var h when h.EndsWith("cmu.edu")        => "CMU",
        var h when h.EndsWith("stanford.edu")   => "Stanford",
        var h when h.EndsWith("berkeley.edu")   => "Berkeley",
        var h when h.EndsWith("oxford.ac.uk")   => "Oxford",
        _ => null,
    };

    private static string ExtractSection(string path)
    {
        var segments = path.TrimStart('/').Split('/', 2);
        return segments.Length > 0 && segments[0].Length > 0 ? "/" + segments[0] : string.Empty;
    }
}
