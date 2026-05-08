using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ConnectorRecipes.UniversitiesSql;

public sealed record UniversityRow(string Name, string Country, int FoundedYear, int Ranking, int StudentsCount, string Website);
public sealed record DepartmentRow(int Id, string University, string Name, string Building, string HeadName, string HeadEmail, List<string> ResearchAreas);
public sealed record ProgramRow(int Id, int DepartmentId, string Name, string DegreeLevel, int DurationYears, string Language, int TuitionUsd);
public sealed record FacultyRow(string Email, int DepartmentId, string Name, string Title, int HIndex, int JoinedYear, List<string> ResearchAreas);

public sealed class UniversitiesSqlLoader
{
    private readonly string _connectionString;

    public UniversitiesSqlLoader(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>Initialize the SQLite database from a seed.sql file if it doesn't already exist.</summary>
    public static void EnsureSeeded(string dbPath, string seedSqlPath)
    {
        if (File.Exists(dbPath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = File.ReadAllText(seedSqlPath);
        cmd.ExecuteNonQuery();
    }

    public List<UniversityRow> LoadUniversities()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, country, COALESCE(founded_year, 0), COALESCE(ranking, 0),
                   COALESCE(students_count, 0), COALESCE(website, '')
              FROM universities
        """;
        using var r = cmd.ExecuteReader();
        var list = new List<UniversityRow>();
        while (r.Read())
            list.Add(new UniversityRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5)));
        return list;
    }

    public List<DepartmentRow> LoadDepartments()
    {
        using var conn = Open();

        var byId = new Dictionary<int, DepartmentRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, university_name, name, COALESCE(building, ''),
                       COALESCE(head_name, ''), COALESCE(head_email, '')
                  FROM departments
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new DepartmentRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), new List<string>());
                byId[row.Id] = row;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT department_id, research_area FROM department_research_areas";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (byId.TryGetValue(r.GetInt32(0), out var dept))
                    dept.ResearchAreas.Add(r.GetString(1));
            }
        }

        return new List<DepartmentRow>(byId.Values);
    }

    public List<ProgramRow> LoadPrograms()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, department_id, name, degree_level, duration_years,
                   COALESCE(language, ''), COALESCE(tuition_usd, 0)
              FROM programs
        """;
        using var r = cmd.ExecuteReader();
        var list = new List<ProgramRow>();
        while (r.Read())
            list.Add(new ProgramRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetString(5), r.GetInt32(6)));
        return list;
    }

    public List<FacultyRow> LoadFaculty()
    {
        using var conn = Open();

        var byEmail = new Dictionary<string, FacultyRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT email, department_id, name, COALESCE(title, ''),
                       COALESCE(h_index, 0), COALESCE(joined_year, 0)
                  FROM faculty
            """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new FacultyRow(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), new List<string>());
                byEmail[row.Email] = row;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT email, research_area FROM faculty_research_areas";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (byEmail.TryGetValue(r.GetString(0), out var fac))
                    fac.ResearchAreas.Add(r.GetString(1));
            }
        }

        return new List<FacultyRow>(byEmail.Values);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
