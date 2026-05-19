using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the JSON model that mirrors each course in the REST
// response, schema registration, and ingestion that links courses to
// subjects, instructors, and terms.
public static class CoursesIngest
{
    public sealed class CourseDoc
    {
        [JsonPropertyName("code")]            public string         Code            { get; set; } = string.Empty;
        [JsonPropertyName("title")]           public string         Title           { get; set; } = string.Empty;
        [JsonPropertyName("level")]           public string         Level           { get; set; } = string.Empty;
        [JsonPropertyName("credits")]         public int            Credits         { get; set; }
        [JsonPropertyName("capacity")]        public int            Capacity        { get; set; }
        [JsonPropertyName("subjects")]        public List<string>   Subjects        { get; set; } = new();
        [JsonPropertyName("terms")]           public List<string>   Terms           { get; set; } = new();
        [JsonPropertyName("instructors")]     public List<InstructorDoc> Instructors { get; set; } = new();
    }

    public sealed class InstructorDoc
    {
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("name")]  public string Name  { get; set; } = string.Empty;
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Course>();
        await graph.CreateNodeSchemaAsync<Nodes.Subject>();
        await graph.CreateNodeSchemaAsync<Nodes.Faculty>();
        await graph.CreateNodeSchemaAsync<Nodes.Term>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, CourseDoc doc)
    {
        var course = graph.AddOrUpdate(new Nodes.Course
        {
            Code     = doc.Code,
            Title    = doc.Title,
            Level    = doc.Level,
            Credits  = doc.Credits,
            Capacity = doc.Capacity,
        });

        foreach (var subjectName in doc.Subjects)
        {
            var subject = graph.TryAdd(new Nodes.Subject { Name = subjectName });
            graph.Link(course, subject, Edges.CoversSubject, Edges.CoveredByCourse);
        }

        foreach (var term in doc.Terms)
        {
            var termNode = graph.TryAdd(new Nodes.Term { Name = term });
            graph.Link(course, termNode, Edges.OfferedIn, Edges.OffersCourse);
        }

        foreach (var instructor in doc.Instructors)
        {
            if (string.IsNullOrWhiteSpace(instructor.Email)) continue;
            var facultyNode = graph.AddOrUpdate(new Nodes.Faculty
            {
                Email = instructor.Email,
                Name  = instructor.Name,
            });
            graph.Link(course, facultyNode, Edges.TaughtBy, Edges.Teaches);
        }
    }
}
