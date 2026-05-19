using System;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: schema registration and the ingestion that turns each
// new feed entry into a NewsItem node with its author / categories / feed /
// university links.
public static class NewsIngest
{
    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.NewsItem>();
        await graph.CreateNodeSchemaAsync<Nodes.Feed>();
        await graph.CreateNodeSchemaAsync<Nodes.Author>();
        await graph.CreateNodeSchemaAsync<Nodes.Category>();
        await graph.CreateNodeSchemaAsync<Nodes.University>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, FeedEntry entry, string feedDisplayName, string? universityName)
    {
        var item = graph.AddOrUpdate(new Nodes.NewsItem
        {
            Id          = entry.EntryId,
            Title       = entry.Title,
            Summary     = entry.Summary,
            Content     = entry.Content,
            Url         = entry.Url,
            PublishedAt = entry.PublishedAt,
        });

        var feed = graph.AddOrUpdate(new Nodes.Feed
        {
            Id   = entry.FeedId,
            Name = feedDisplayName,
        });
        graph.Link(item, feed, Edges.PublishedIn, Edges.Publishes);

        if (!string.IsNullOrWhiteSpace(entry.AuthorName))
        {
            var author = graph.AddOrUpdate(new Nodes.Author
            {
                Name  = entry.AuthorName,
                Email = entry.AuthorEmail,
            });
            graph.Link(author, item, Edges.Wrote, Edges.WrittenBy);
        }

        foreach (var cat in entry.Categories)
        {
            if (string.IsNullOrWhiteSpace(cat)) continue;
            var node = graph.TryAdd(new Nodes.Category { Name = cat });
            graph.Link(item, node, Edges.CategorizedAs, Edges.CategoryOf);
        }

        if (!string.IsNullOrWhiteSpace(universityName))
        {
            var university = graph.TryAdd(new Nodes.University { Name = universityName });
            graph.Link(item, university, Edges.About, Edges.NewsAbout);
        }
    }
}
