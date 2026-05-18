namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Subject keys match the CSV sample so the
// graphs merge on Subject.Name; this connector adds Topics, Books, Authors.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Subject
        {
            [Key]      public string Name        { get; set; } = string.Empty;
            [Property] public string Level       { get; set; } = string.Empty;
            [Property] public string Description { get; set; } = string.Empty;
        }

        [Node]
        public class Topic
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Book
        {
            [Key]      public string Isbn    { get; set; } = string.Empty;
            [Property] public string Title   { get; set; } = string.Empty;
            [Property] public int    Year    { get; set; }
            [Property] public int    Edition { get; set; }
        }

        [Node]
        public class Author
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string Covers         = nameof(Covers);
        public const string CoveredIn      = nameof(CoveredIn);

        public const string RecommendsBook = nameof(RecommendsBook);
        public const string RecommendedFor = nameof(RecommendedFor);

        public const string WrittenBy      = nameof(WrittenBy);
        public const string Wrote          = nameof(Wrote);
    }
}
