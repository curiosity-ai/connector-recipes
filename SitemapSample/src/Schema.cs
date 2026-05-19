using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema. University.Name matches CSV/SQL samples so
// scraped university pages attach to the same university nodes; Tag.Name
// is left distinct from Skill (these are page-keyword vocabularies, not
// person-skills, even though they sometimes overlap).
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class WebPage
        {
            [Key]      public string         Url         { get; set; } = string.Empty;
            [Property] public string         Title       { get; set; } = string.Empty;
            [Property] public string         H1          { get; set; } = string.Empty;
            [Property] public string         Description { get; set; } = string.Empty;
            [Property] public string         Content     { get; set; } = string.Empty;
            [Property] public string         ContentHash { get; set; } = string.Empty;
            [Property] public int            StatusCode  { get; set; }
            [Timestamp] public DateTimeOffset LastModified { get; set; }
        }

        [Node]
        public class Website
        {
            [Key]      public string Host { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class University
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Tag
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Section
        {
            [Key]      public string Path { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string HostedOn   = nameof(HostedOn);
        public const string Hosts      = nameof(Hosts);

        public const string AboutUniversity = nameof(AboutUniversity);
        public const string WebPagesAbout   = nameof(WebPagesAbout);

        public const string TaggedWith = nameof(TaggedWith);
        public const string TagOf      = nameof(TagOf);

        public const string InSection  = nameof(InSection);
        public const string Contains   = nameof(Contains);
    }
}
