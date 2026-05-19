namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Student.Id and Skill.Name match the CSV and JSON
// samples — this connector enriches existing students with internships,
// projects, and company links pulled from a Mongo profiles collection.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Student
        {
            [Key]      public string Id        { get; set; } = string.Empty;
            [Property] public string Bio       { get; set; } = string.Empty;
            [Property] public string GithubUrl { get; set; } = string.Empty;
            [Property] public string LinkedIn  { get; set; } = string.Empty;
        }

        [Node]
        public class Skill
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Internship
        {
            [Key]      public string Id        { get; set; } = string.Empty;
            [Property] public string Role      { get; set; } = string.Empty;
            [Property] public int    StartYear { get; set; }
            [Property] public int    EndYear   { get; set; }
        }

        [Node]
        public class Company
        {
            [Key]      public string Name     { get; set; } = string.Empty;
            [Property] public string Industry { get; set; } = string.Empty;
            [Property] public string Website  { get; set; } = string.Empty;
        }

        [Node]
        public class Project
        {
            [Key]      public string Id          { get; set; } = string.Empty;
            [Property] public string Name        { get; set; } = string.Empty;
            [Property] public string Description { get; set; } = string.Empty;
            [Property] public string Url         { get; set; } = string.Empty;
        }

        [Node]
        public class Interest
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string Held       = nameof(Held);
        public const string HeldBy     = nameof(HeldBy);

        public const string At         = nameof(At);
        public const string Hosted     = nameof(Hosted);

        public const string Built      = nameof(Built);
        public const string BuiltBy    = nameof(BuiltBy);

        public const string Uses       = nameof(Uses);
        public const string UsedIn     = nameof(UsedIn);

        public const string InterestedIn   = nameof(InterestedIn);
        public const string InterestOf     = nameof(InterestOf);
    }
}
