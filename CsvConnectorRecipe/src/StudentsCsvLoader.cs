using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

namespace ConnectorRecipes.Csv;

public sealed class StudentRow
{
    [Name("student_id")]        public string StudentId      { get; set; } = string.Empty;
    [Name("student_name")]      public string StudentName    { get; set; } = string.Empty;
    [Name("birth_year")]        public int    BirthYear      { get; set; }
    [Name("enrollment_year")]   public int    EnrollmentYear { get; set; }
    [Name("gpa")]               public double Gpa            { get; set; }
    [Name("university")]        public string University     { get; set; } = string.Empty;
    [Name("department")]        public string Department     { get; set; } = string.Empty;
    [Name("degree")]            public string Degree         { get; set; } = string.Empty;
    [Name("major")]             public string Major          { get; set; } = string.Empty;
    [Name("advisor_name")]      public string AdvisorName    { get; set; } = string.Empty;
    [Name("advisor_email")]     public string AdvisorEmail   { get; set; } = string.Empty;
    [Name("subjects")]          public string SubjectsRaw    { get; set; } = string.Empty;
    [Name("skills")]            public string SkillsRaw      { get; set; } = string.Empty;
    [Name("country")]           public string Country        { get; set; } = string.Empty;
    [Name("city")]              public string City           { get; set; } = string.Empty;

    public IEnumerable<string> Subjects => Split(SubjectsRaw);
    public IEnumerable<string> Skills   => Split(SkillsRaw);

    private static IEnumerable<string> Split(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split('|'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) yield return trimmed;
        }
    }
}

public static class StudentsCsvLoader
{
    public static IReadOnlyList<StudentRow> Load(string path)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions     = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, config);

        var rows = new List<StudentRow>();
        foreach (var row in csv.GetRecords<StudentRow>())
        {
            rows.Add(row);
        }
        return rows;
    }
}
