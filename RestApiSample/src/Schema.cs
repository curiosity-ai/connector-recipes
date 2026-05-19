namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Subject.Name and Faculty.Email match the CSV/SQL
// samples so this connector enriches the same students-and-subjects graph
// with courses, terms, and instructors.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Course
        {
            [Key]      public string Code      { get; set; } = string.Empty;
            [Property] public string Title     { get; set; } = string.Empty;
            [Property] public string Level     { get; set; } = string.Empty;
            [Property] public int    Credits   { get; set; }
            [Property] public int    Capacity  { get; set; }
        }

        [Node]
        public class Subject
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Same key as CSV's Advisor / SQL's Faculty so emails merge.
        [Node]
        public class Faculty
        {
            [Key]      public string Email { get; set; } = string.Empty;
            [Property] public string Name  { get; set; } = string.Empty;
        }

        [Node]
        public class Term
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string CoversSubject = nameof(CoversSubject);
        public const string CoveredByCourse = nameof(CoveredByCourse);

        public const string TaughtBy   = nameof(TaughtBy);
        public const string Teaches    = nameof(Teaches);

        public const string OfferedIn  = nameof(OfferedIn);
        public const string OffersCourse = nameof(OffersCourse);
    }
}
