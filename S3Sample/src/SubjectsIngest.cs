using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: subject + book document models, schema registration,
// and the ingestion that joins subjects to books via ISBN.
public static class SubjectsIngest
{
    public sealed class SubjectDoc
    {
        [JsonPropertyName("name")]        public string       Name        { get; set; } = string.Empty;
        [JsonPropertyName("level")]       public string       Level       { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string       Description { get; set; } = string.Empty;
        [JsonPropertyName("topics")]      public List<string> Topics      { get; set; } = new();
        [JsonPropertyName("bookIsbns")]   public List<string> BookIsbns   { get; set; } = new();
    }

    public sealed class BookDoc
    {
        [JsonPropertyName("isbn")]    public string       Isbn    { get; set; } = string.Empty;
        [JsonPropertyName("title")]   public string       Title   { get; set; } = string.Empty;
        [JsonPropertyName("year")]    public int          Year    { get; set; }
        [JsonPropertyName("edition")] public int          Edition { get; set; }
        [JsonPropertyName("authors")] public List<string> Authors { get; set; } = new();
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Subject>();
        await graph.CreateNodeSchemaAsync<Nodes.Topic>();
        await graph.CreateNodeSchemaAsync<Nodes.Book>();
        await graph.CreateNodeSchemaAsync<Nodes.Author>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static async Task IngestAsync(Graph graph, IObjectStore store)
    {
        var subjects = await ReadAllAsync<SubjectDoc>(store, "subjects/");
        var books    = await ReadAllAsync<BookDoc>(store, "books/");
        var booksByIsbn = books.ToDictionary(b => b.Isbn, b => b);

        foreach (var s in subjects)
        {
            var subject = graph.AddOrUpdate(new Nodes.Subject
            {
                Name        = s.Name,
                Level       = s.Level,
                Description = s.Description,
            });

            foreach (var topic in s.Topics)
            {
                var topicNode = graph.TryAdd(new Nodes.Topic { Name = topic });
                graph.Link(subject, topicNode, Edges.Covers, Edges.CoveredIn);
            }

            foreach (var isbn in s.BookIsbns)
            {
                if (!booksByIsbn.TryGetValue(isbn, out var book)) continue;

                var bookNode = graph.AddOrUpdate(new Nodes.Book
                {
                    Isbn    = book.Isbn,
                    Title   = book.Title,
                    Year    = book.Year,
                    Edition = book.Edition,
                });
                graph.Link(subject, bookNode, Edges.RecommendsBook, Edges.RecommendedFor);

                foreach (var author in book.Authors)
                {
                    var authorNode = graph.TryAdd(new Nodes.Author { Name = author });
                    graph.Link(bookNode, authorNode, Edges.WrittenBy, Edges.Wrote);
                }
            }
        }
    }

    private static async Task<List<T>> ReadAllAsync<T>(IObjectStore store, string prefix)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = new List<T>();
        await foreach (var (_, json) in store.ListAsync(prefix))
        {
            var item = JsonSerializer.Deserialize<T>(json, opts);
            if (item is not null) list.Add(item);
        }
        return list;
    }
}
