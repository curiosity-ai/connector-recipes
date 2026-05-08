using System;
using Curiosity.Library;

namespace ConnectorRecipes.SkillsJson;

public static class Schema
{
    public static class Nodes
    {
        // Same key as CsvConnectorRecipe's Skill node — this recipe just adds
        // properties to it. The graph server merges the schemas.
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

        // Symmetric "related to" — same name in both directions.
        public const string RelatedToSkill = nameof(RelatedToSkill);

        public const string Teaches        = nameof(Teaches);
        public const string TaughtBy       = nameof(TaughtBy);
    }
}
