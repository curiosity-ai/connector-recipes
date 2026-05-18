using System.Collections.Generic;
using System.Threading.Tasks;
using CsvHelper.Configuration.Attributes;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the row model that mirrors students.csv columns,
// the schema registration, and the per-row ingestion logic.
public static class StudentsIngest
{
    public sealed class Row
    {
        [Name("student_id")]      public string StudentId      { get; set; } = string.Empty;
        [Name("student_name")]    public string StudentName    { get; set; } = string.Empty;
        [Name("birth_year")]      public int    BirthYear      { get; set; }
        [Name("enrollment_year")] public int    EnrollmentYear { get; set; }
        [Name("gpa")]             public double Gpa            { get; set; }
        [Name("university")]      public string University     { get; set; } = string.Empty;
        [Name("department")]      public string Department     { get; set; } = string.Empty;
        [Name("degree")]          public string Degree         { get; set; } = string.Empty;
        [Name("major")]           public string Major          { get; set; } = string.Empty;
        [Name("advisor_name")]    public string AdvisorName    { get; set; } = string.Empty;
        [Name("advisor_email")]   public string AdvisorEmail   { get; set; } = string.Empty;
        [Name("subjects")]        public string SubjectsRaw    { get; set; } = string.Empty;
        [Name("skills")]          public string SkillsRaw      { get; set; } = string.Empty;
        [Name("country")]         public string Country        { get; set; } = string.Empty;
        [Name("city")]            public string City           { get; set; } = string.Empty;

        public IEnumerable<string> Subjects => SplitPipe(SubjectsRaw);
        public IEnumerable<string> Skills   => SplitPipe(SkillsRaw);

        private static IEnumerable<string> SplitPipe(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;
            foreach (var part in raw.Split('|'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Student>();
        await graph.CreateNodeSchemaAsync<Nodes.University>();
        await graph.CreateNodeSchemaAsync<Nodes.Department>();
        await graph.CreateNodeSchemaAsync<Nodes.Degree>();
        await graph.CreateNodeSchemaAsync<Nodes.Major>();
        await graph.CreateNodeSchemaAsync<Nodes.Subject>();
        await graph.CreateNodeSchemaAsync<Nodes.Skill>();
        await graph.CreateNodeSchemaAsync<Nodes.Advisor>();
        await graph.CreateNodeSchemaAsync<Nodes.Country>();
        await graph.CreateNodeSchemaAsync<Nodes.City>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, Row row)
    {
        var student = graph.AddOrUpdate(new Nodes.Student
        {
            Id             = row.StudentId,
            Name           = row.StudentName,
            BirthYear      = row.BirthYear,
            EnrollmentYear = row.EnrollmentYear,
            Gpa            = row.Gpa,
        });

        var university = graph.TryAdd(new Nodes.University { Name = row.University });
        graph.Link(student, university, Edges.EnrolledAt, Edges.EnrolledStudent);

        var department = graph.TryAdd(new Nodes.Department
        {
            Id   = $"{row.University}/{row.Department}",
            Name = row.Department,
        });
        graph.Link(department, university, Edges.PartOf, Edges.HasDepartment);
        graph.Link(student, department, Edges.BelongsToDepartment, Edges.HasMember);

        var degree = graph.TryAdd(new Nodes.Degree { Name = row.Degree });
        graph.Link(student, degree, Edges.PursuesDegree, Edges.PursuedBy);

        var major = graph.TryAdd(new Nodes.Major { Name = row.Major });
        graph.Link(major, department, Edges.OfferedBy, Edges.Offers);
        graph.Link(student, major, Edges.HasMajor, Edges.MajorOf);

        var advisor = graph.AddOrUpdate(new Nodes.Advisor
        {
            Email = row.AdvisorEmail,
            Name  = row.AdvisorName,
        });
        graph.Link(advisor, department, Edges.WorksIn, Edges.Employs);
        graph.Link(student, advisor, Edges.AdvisedBy, Edges.Advises);

        foreach (var subject in row.Subjects)
        {
            var subjectNode = graph.TryAdd(new Nodes.Subject { Name = subject });
            graph.Link(student, subjectNode, Edges.Studies, Edges.StudiedBy);
        }

        foreach (var skill in row.Skills)
        {
            var skillNode = graph.TryAdd(new Nodes.Skill { Name = skill });
            graph.Link(student, skillNode, Edges.HasSkill, Edges.SkillOf);
        }

        var country = graph.TryAdd(new Nodes.Country { Name = row.Country });
        var city    = graph.TryAdd(new Nodes.City
        {
            Id   = $"{row.City}, {row.Country}",
            Name = row.City,
        });
        graph.Link(city, country, Edges.In, Edges.Includes);
        graph.Link(student, city, Edges.LivesIn, Edges.Resident);
    }
}
