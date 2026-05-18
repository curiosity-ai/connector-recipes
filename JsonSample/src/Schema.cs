namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Skill keys match the CSV sample so the two
// graphs merge on Skill.Name; this connector adds properties and edges.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Skill
        {
            [Key]      public string Name           { get; set; } = string.Empty;
            [Property] public string Description    { get; set; } = string.Empty;
            [Property] public double Popularity     { get; set; }
            [Property] public int    YearIntroduced { get; set; }
        }

        [Node]
        public class SkillCategory
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class LearningResource
        {
            [Key]      public string Url   { get; set; } = string.Empty;
            [Property] public string Title { get; set; } = string.Empty;
            [Property] public string Kind  { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string HasCategory    = nameof(HasCategory);
        public const string CategoryOf     = nameof(CategoryOf);

        public const string RequiresSkill  = nameof(RequiresSkill);
        public const string RequiredBy     = nameof(RequiredBy);

        // Symmetric: same name in both directions.
        public const string RelatedToSkill = nameof(RelatedToSkill);

        public const string Teaches        = nameof(Teaches);
        public const string TaughtBy       = nameof(TaughtBy);
    }
}
