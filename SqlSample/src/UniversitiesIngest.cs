using System.Collections.Generic;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: row records mirroring the seed schema, SELECT statements,
// schema registration, and the ingestion that joins universities → departments
// → programs → faculty → research areas.
public static class UniversitiesIngest
{
    public sealed record UniversityRow(string Name, string Country, int FoundedYear, int Ranking, int StudentsCount, string Website);
    public sealed record DepartmentRow(int Id, string University, string Name, string Building, string HeadName, string HeadEmail);
    public sealed record ProgramRow(int DepartmentId, string Name, string DegreeLevel, int DurationYears, string Language, int TuitionUsd);
    public sealed record FacultyRow(string Email, int DepartmentId, string Name, string Title, int HIndex, int JoinedYear);
    public sealed record AreaLink<T>(T OwnerKey, string Area);

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.University>();
        await graph.CreateNodeSchemaAsync<Nodes.Country>();
        await graph.CreateNodeSchemaAsync<Nodes.Department>();
        await graph.CreateNodeSchemaAsync<Nodes.Program>();
        await graph.CreateNodeSchemaAsync<Nodes.Faculty>();
        await graph.CreateNodeSchemaAsync<Nodes.ResearchArea>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, SqliteSource db)
    {
        var universities = db.Query(
            "SELECT name, country, COALESCE(founded_year,0), COALESCE(ranking,0), " +
            "       COALESCE(students_count,0), COALESCE(website,'') FROM universities",
            r => new UniversityRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5)));

        var departments = db.Query(
            "SELECT id, university_name, name, COALESCE(building,''), " +
            "       COALESCE(head_name,''), COALESCE(head_email,'') FROM departments",
            r => new DepartmentRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));

        var programs = db.Query(
            "SELECT department_id, name, degree_level, duration_years, " +
            "       COALESCE(language,''), COALESCE(tuition_usd,0) FROM programs",
            r => new ProgramRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetInt32(5)));

        var faculty = db.Query(
            "SELECT email, department_id, name, COALESCE(title,''), " +
            "       COALESCE(h_index,0), COALESCE(joined_year,0) FROM faculty",
            r => new FacultyRow(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5)));

        var deptAreas = db.Query(
            "SELECT department_id, research_area FROM department_research_areas",
            r => new AreaLink<int>(r.GetInt32(0), r.GetString(1)));

        var facultyAreas = db.Query(
            "SELECT email, research_area FROM faculty_research_areas",
            r => new AreaLink<string>(r.GetString(0), r.GetString(1)));

        foreach (var u in universities)
        {
            var university = graph.AddOrUpdate(new Nodes.University
            {
                Name          = u.Name,
                FoundedYear   = u.FoundedYear,
                Ranking       = u.Ranking,
                StudentsCount = u.StudentsCount,
                Website       = u.Website,
            });
            var country = graph.TryAdd(new Nodes.Country { Name = u.Country });
            graph.Link(university, country, Edges.BasedIn, Edges.Hosts);
        }

        var deptIdToKey = new Dictionary<int, string>();
        foreach (var d in departments)
        {
            var key = $"{d.University}/{d.Name}";
            deptIdToKey[d.Id] = key;

            var department = graph.AddOrUpdate(new Nodes.Department
            {
                Id        = key,
                Name      = d.Name,
                Building  = d.Building,
                HeadName  = d.HeadName,
                HeadEmail = d.HeadEmail,
            });
            graph.Link(department, Node.FromKey(nameof(Nodes.University), d.University), Edges.PartOf, Edges.HasDepartment);
        }

        foreach (var link in deptAreas)
        {
            if (!deptIdToKey.TryGetValue(link.OwnerKey, out var deptKey)) continue;
            var area = graph.TryAdd(new Nodes.ResearchArea { Name = link.Area });
            graph.Link(Node.FromKey(nameof(Nodes.Department), deptKey), area, Edges.HasResearchArea, Edges.ResearchAreaOf);
        }

        foreach (var p in programs)
        {
            if (!deptIdToKey.TryGetValue(p.DepartmentId, out var deptKey)) continue;
            var programKey = $"{deptKey}/{p.Name}";
            var program = graph.AddOrUpdate(new Nodes.Program
            {
                Id            = programKey,
                Name          = p.Name,
                DegreeLevel   = p.DegreeLevel,
                DurationYears = p.DurationYears,
                Language      = p.Language,
                TuitionUsd    = p.TuitionUsd,
            });
            graph.Link(Node.FromKey(nameof(Nodes.Department), deptKey), program, Edges.OffersProgram, Edges.ProgramOf);
        }

        foreach (var f in faculty)
        {
            if (!deptIdToKey.TryGetValue(f.DepartmentId, out var deptKey)) continue;
            var facultyNode = graph.AddOrUpdate(new Nodes.Faculty
            {
                Email      = f.Email,
                Name       = f.Name,
                Title      = f.Title,
                HIndex     = f.HIndex,
                JoinedYear = f.JoinedYear,
            });
            graph.Link(facultyNode, Node.FromKey(nameof(Nodes.Department), deptKey), Edges.TeachesIn, Edges.HasFaculty);
        }

        foreach (var link in facultyAreas)
        {
            var area = graph.TryAdd(new Nodes.ResearchArea { Name = link.Area });
            graph.Link(Node.FromKey(nameof(Nodes.Faculty), link.OwnerKey), area, Edges.Researches, Edges.ResearchedBy);
        }
    }
}
