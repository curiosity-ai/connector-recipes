using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Faculty.Email matches CSV's Advisor / SQL sample's
// Faculty; University.Name and ResearchArea.Name match the SQL sample so
// grants merge into the same institutional graph.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Grant
        {
            [Key]       public string         Id           { get; set; } = string.Empty;
            [Property]  public string         Title        { get; set; } = string.Empty;
            [Property]  public long           AmountUsd    { get; set; }
            [Property]  public int            StartYear    { get; set; }
            [Property]  public int            EndYear      { get; set; }
            [Property]  public string         Status       { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset AwardedAt    { get; set; }
        }

        [Node]
        public class Faculty
        {
            [Key]      public string Email { get; set; } = string.Empty;
            [Property] public string Name  { get; set; } = string.Empty;
        }

        [Node]
        public class University
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class ResearchArea
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class FundingAgency
        {
            [Key]      public string Acronym { get; set; } = string.Empty;
            [Property] public string Name    { get; set; } = string.Empty;
            [Property] public string Country { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string AwardedTo      = nameof(AwardedTo);
        public const string Holds          = nameof(Holds);

        public const string FundedBy       = nameof(FundedBy);
        public const string Funds          = nameof(Funds);

        public const string AffiliatedWith = nameof(AffiliatedWith);
        public const string Affiliates     = nameof(Affiliates);

        public const string Covers         = nameof(Covers);
        public const string CoveredBy      = nameof(CoveredBy);
    }
}
