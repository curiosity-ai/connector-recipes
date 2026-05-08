using System;
using Curiosity.Library;

namespace ConnectorRecipes.UniversitiesSql;

public static class Schema
{
    public static class Nodes
    {
        // Same key as the CSV recipe's University — this recipe extends it
        // with metadata pulled from the relational source.
        [Node]
        public class University
        {
            [Key]      public string Name           { get; set; } = string.Empty;
            [Property] public int    FoundedYear    { get; set; }
            [Property] public int    Ranking        { get; set; }
            [Property] public int    StudentsCount  { get; set; }
            [Property] public string Website        { get; set; } = string.Empty;
        }

        [Node]
        public class Country
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Composite key matches CsvConnectorRecipe so the same Department node
        // is referenced from both connectors.
        [Node]
        public class Department
        {
            [Key]      public string Id        { get; set; } = string.Empty;
            [Property] public string Name      { get; set; } = string.Empty;
            [Property] public string Building  { get; set; } = string.Empty;
            [Property] public string HeadName  { get; set; } = string.Empty;
            [Property] public string HeadEmail { get; set; } = string.Empty;
        }

        [Node]
        public class Program
        {
            [Key]      public string Id            { get; set; } = string.Empty;
            [Property] public string Name          { get; set; } = string.Empty;
            [Property] public string DegreeLevel   { get; set; } = string.Empty;
            [Property] public int    DurationYears { get; set; }
            [Property] public string Language      { get; set; } = string.Empty;
            [Property] public int    TuitionUsd    { get; set; }
        }

        // Same key as CsvConnectorRecipe's Advisor — the SQL source has
        // richer metadata (title, h-index, joined year), but they merge
        // onto the same node by email.
        [Node]
        public class Faculty
        {
            [Key]      public string Email      { get; set; } = string.Empty;
            [Property] public string Name       { get; set; } = string.Empty;
            [Property] public string Title      { get; set; } = string.Empty;
            [Property] public int    HIndex     { get; set; }
            [Property] public int    JoinedYear { get; set; }
        }

        [Node]
        public class ResearchArea
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string BasedIn            = nameof(BasedIn);
        public const string Hosts              = nameof(Hosts);

        // CSV recipe already pairs Department↔University via PartOf/HasDepartment.
        // Reusing the same names keeps the graph consistent.
        public const string PartOf             = nameof(PartOf);
        public const string HasDepartment      = nameof(HasDepartment);

        public const string OffersProgram      = nameof(OffersProgram);
        public const string ProgramOf          = nameof(ProgramOf);

        public const string TeachesIn          = nameof(TeachesIn);
        public const string HasFaculty         = nameof(HasFaculty);

        public const string Researches         = nameof(Researches);
        public const string ResearchedBy       = nameof(ResearchedBy);

        public const string HasResearchArea    = nameof(HasResearchArea);
        public const string ResearchAreaOf     = nameof(ResearchAreaOf);
    }
}
