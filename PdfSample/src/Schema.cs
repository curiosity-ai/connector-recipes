using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema for industrial maintenance manuals. Self-contained
// graph: Equipment ← documented by → Manual → Procedure → Part ← supplied by
// → Manufacturer; procedures performed by → Technician.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Equipment
        {
            [Key]      public string Id            { get; set; } = string.Empty;
            [Property] public string Model         { get; set; } = string.Empty;
            [Property] public string Category      { get; set; } = string.Empty;
            [Property] public string SerialNumber  { get; set; } = string.Empty;
            [Property] public string Plant         { get; set; } = string.Empty;
        }

        [Node]
        public class Manual
        {
            [Key]      public string         DocumentNumber { get; set; } = string.Empty;
            [Property] public string         Title          { get; set; } = string.Empty;
            [Property] public string         SourceFile     { get; set; } = string.Empty;
            [Property] public string         ContentHash    { get; set; } = string.Empty;
            [Property] public string         Revision       { get; set; } = string.Empty;
            [Property] public int            PageCount      { get; set; }
            [Timestamp] public DateTimeOffset PublishedAt   { get; set; }
        }

        [Node]
        public class ManualPage
        {
            [Key]      public string Id      { get; set; } = string.Empty;
            [Property] public int    Number  { get; set; }
            [Property] public string Heading { get; set; } = string.Empty;
            [Property] public string Content { get; set; } = string.Empty;
        }

        [Node]
        public class TextChunk
        {
            [Key]      public string Id       { get; set; } = string.Empty;
            [Property] public int    PageNum  { get; set; }
            [Property] public string Content  { get; set; } = string.Empty;
        }

        [Node]
        public class Procedure
        {
            [Key]      public string Id       { get; set; } = string.Empty;
            [Property] public string Name     { get; set; } = string.Empty;
            [Property] public string Category { get; set; } = string.Empty;
            [Property] public string Severity { get; set; } = string.Empty;
        }

        [Node]
        public class Part
        {
            [Key]      public string PartNumber { get; set; } = string.Empty;
            [Property] public string Name       { get; set; } = string.Empty;
            [Property] public string Category   { get; set; } = string.Empty;
        }

        [Node]
        public class Manufacturer
        {
            [Key]      public string Name    { get; set; } = string.Empty;
            [Property] public string Country { get; set; } = string.Empty;
        }

        [Node]
        public class Technician
        {
            [Key]      public string EmployeeId { get; set; } = string.Empty;
            [Property] public string Name       { get; set; } = string.Empty;
            [Property] public string Trade      { get; set; } = string.Empty;
        }

        [Node]
        public class SafetyHazard
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string DocumentedBy   = nameof(DocumentedBy);
        public const string Documents      = nameof(Documents);

        public const string HasPage        = nameof(HasPage);
        public const string PageOf         = nameof(PageOf);

        public const string ChunkedFrom    = nameof(ChunkedFrom);
        public const string ChunkOf        = nameof(ChunkOf);

        public const string Describes      = nameof(Describes);
        public const string DescribedIn    = nameof(DescribedIn);

        public const string RequiresPart   = nameof(RequiresPart);
        public const string UsedIn         = nameof(UsedIn);

        public const string SuppliedBy     = nameof(SuppliedBy);
        public const string Supplies       = nameof(Supplies);

        public const string AuthoredBy     = nameof(AuthoredBy);
        public const string Authored       = nameof(Authored);

        public const string PerformedBy    = nameof(PerformedBy);
        public const string Performs       = nameof(Performs);

        public const string WarnsAbout     = nameof(WarnsAbout);
        public const string AppearsIn      = nameof(AppearsIn);

        public const string Maintains      = nameof(Maintains);
        public const string MaintainedBy   = nameof(MaintainedBy);
    }
}
