using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: schema registration and the ingestion that maps each
// row from the Parquet/Avro file into a Grade node plus its links.
public static class GradesIngest
{
    // The columns the connector reads. Listing them up front lets the
    // columnar source skip the others entirely — important when the file
    // has 50+ columns but ingestion only needs 6.
    public static readonly string[] Columns = new[]
    {
        "student_id", "course_code", "subject", "term", "letter_grade", "gpa_points", "credit_hours",
    };

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Student>();
        await graph.CreateNodeSchemaAsync<Nodes.Course>();
        await graph.CreateNodeSchemaAsync<Nodes.Subject>();
        await graph.CreateNodeSchemaAsync<Nodes.Term>();
        await graph.CreateNodeSchemaAsync<Nodes.Grade>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, ColumnarRow row)
    {
        var studentId   = row.Get<string>("student_id")   ?? string.Empty;
        var courseCode  = row.Get<string>("course_code")  ?? string.Empty;
        var subjectName = row.Get<string>("subject")      ?? string.Empty;
        var termName    = row.Get<string>("term")         ?? string.Empty;
        var letter      = row.Get<string>("letter_grade") ?? string.Empty;
        var gpaPoints   = row.Get<double>("gpa_points");
        var credits     = row.Get<int>("credit_hours");

        var gradeKey = $"{studentId}/{courseCode}/{termName}";
        var grade = graph.AddOrUpdate(new Nodes.Grade
        {
            Id          = gradeKey,
            Letter      = letter,
            GpaPoints   = gpaPoints,
            CreditHours = credits,
        });

        var student = graph.TryAdd(new Nodes.Student { Id = studentId });
        graph.Link(student, grade, Edges.Received, Edges.ReceivedBy);

        var course = graph.TryAdd(new Nodes.Course { Code = courseCode });
        graph.Link(grade, course, Edges.ForCourse, Edges.CourseOf);

        if (!string.IsNullOrWhiteSpace(subjectName))
        {
            var subject = graph.TryAdd(new Nodes.Subject { Name = subjectName });
            graph.Link(course, subject, Edges.CoversSubject, Edges.CoveredBy);
        }

        if (!string.IsNullOrWhiteSpace(termName))
        {
            var term = graph.TryAdd(new Nodes.Term { Name = termName });
            graph.Link(grade, term, Edges.DuringTerm, Edges.TermOf);
        }
    }
}
