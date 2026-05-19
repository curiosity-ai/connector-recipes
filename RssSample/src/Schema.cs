using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema. University.Name matches CSV/SQL/Sitemap samples
// so news items attach to the same universities; Author.Name matches the
// S3 sample's book author key, which is useful if a faculty member writes
// both a book and a press release.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class NewsItem
        {
            [Key]       public string         Id          { get; set; } = string.Empty;
            [Property]  public string         Title       { get; set; } = string.Empty;
            [Property]  public string         Summary     { get; set; } = string.Empty;
            [Property]  public string         Content     { get; set; } = string.Empty;
            [Property]  public string         Url         { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset PublishedAt { get; set; }
        }

        [Node]
        public class Feed
        {
            [Key]      public string Id   { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Author
        {
            [Key]      public string Name  { get; set; } = string.Empty;
            [Property] public string Email { get; set; } = string.Empty;
        }

        [Node]
        public class Category
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class University
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string PublishedIn   = nameof(PublishedIn);
        public const string Publishes     = nameof(Publishes);

        public const string Wrote         = nameof(Wrote);
        public const string WrittenBy     = nameof(WrittenBy);

        public const string CategorizedAs = nameof(CategorizedAs);
        public const string CategoryOf    = nameof(CategoryOf);

        public const string About         = nameof(About);
        public const string NewsAbout     = nameof(NewsAbout);
    }
}
