using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the event model, schema registration, and the
// idempotent-upsert ingestion that turns each stream event into a graph
// mutation. Idempotency comes from using a deterministic key derived from
// the event payload — replaying the same event lands on the same node.
public static class EnrollmentsIngest
{
    public sealed class EnrollmentEvent
    {
        [JsonPropertyName("eventType")]    public string         EventType    { get; set; } = string.Empty;
        [JsonPropertyName("studentId")]    public string         StudentId    { get; set; } = string.Empty;
        [JsonPropertyName("courseCode")]   public string         CourseCode   { get; set; } = string.Empty;
        [JsonPropertyName("term")]         public string         Term         { get; set; } = string.Empty;
        [JsonPropertyName("status")]       public string         Status       { get; set; } = string.Empty;
        [JsonPropertyName("grade")]        public string         Grade        { get; set; } = string.Empty;
        [JsonPropertyName("occurredAt")]   public DateTimeOffset OccurredAt   { get; set; }
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Student>();
        await graph.CreateNodeSchemaAsync<Nodes.Course>();
        await graph.CreateNodeSchemaAsync<Nodes.Term>();
        await graph.CreateNodeSchemaAsync<Nodes.Enrollment>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, EnrollmentEvent evt)
    {
        var key = ComposeKey(evt);

        var enrollment = graph.AddOrUpdate(new Nodes.Enrollment
        {
            Id        = key,
            Status    = evt.Status,
            Grade     = evt.Grade,
            UpdatedAt = evt.OccurredAt,
        });

        var student = graph.TryAdd(new Nodes.Student { Id = evt.StudentId });
        graph.Link(student, enrollment, Edges.HasEnrollment, Edges.EnrolledIn);

        var course = graph.TryAdd(new Nodes.Course { Code = evt.CourseCode });
        graph.Link(enrollment, course, Edges.ForCourse, Edges.CourseOf);

        var term = graph.TryAdd(new Nodes.Term { Name = evt.Term });
        graph.Link(enrollment, term, Edges.DuringTerm, Edges.TermOf);
    }

    // Composite key — deterministic from the event content, so replaying
    // the same event lands on the same node and the graph converges to the
    // last-write-wins state regardless of replay history.
    public static string ComposeKey(EnrollmentEvent evt)
        => $"{evt.StudentId}/{evt.CourseCode}/{evt.Term}";
}
