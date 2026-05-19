using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Skill.Name matches the CSV/JSON samples — GitHub
// topic tags merge into the same skill vocabulary, so a "Python" topic on a
// repo connects to "Python" skills students hold and to learning resources.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Repository
        {
            [Key]      public string         NameWithOwner { get; set; } = string.Empty;
            [Property] public string         Description   { get; set; } = string.Empty;
            [Property] public string         Url           { get; set; } = string.Empty;
            [Property] public string         PrimaryLanguage { get; set; } = string.Empty;
            [Property] public int            Stars         { get; set; }
            [Property] public int            Forks         { get; set; }
            [Property] public bool           IsArchived    { get; set; }
            [Timestamp] public DateTimeOffset CreatedAt    { get; set; }
        }

        [Node]
        public class GitHubUser
        {
            [Key]      public string Login    { get; set; } = string.Empty;
            [Property] public string Name     { get; set; } = string.Empty;
            [Property] public string Company  { get; set; } = string.Empty;
            [Property] public string Location { get; set; } = string.Empty;
        }

        [Node]
        public class Issue
        {
            [Key]      public string         Id        { get; set; } = string.Empty;
            [Property] public int            Number    { get; set; }
            [Property] public string         Title     { get; set; } = string.Empty;
            [Property] public string         State     { get; set; } = string.Empty;
            [Property] public string         Url       { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset CreatedAt { get; set; }
        }

        [Node]
        public class PullRequest
        {
            [Key]      public string         Id        { get; set; } = string.Empty;
            [Property] public int            Number    { get; set; }
            [Property] public string         Title     { get; set; } = string.Empty;
            [Property] public string         State     { get; set; } = string.Empty;
            [Property] public string         Url       { get; set; } = string.Empty;
            [Property] public bool           Merged    { get; set; }
            [Timestamp] public DateTimeOffset CreatedAt { get; set; }
        }

        [Node]
        public class Review
        {
            [Key]      public string Id    { get; set; } = string.Empty;
            [Property] public string State { get; set; } = string.Empty;
        }

        [Node]
        public class Label
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Same key as CSV/JSON Skill — GitHub topics ("python", "rust", ...)
        // are an existing skill vocabulary in disguise.
        [Node]
        public class Skill
        {
            [Key] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string OwnedBy        = nameof(OwnedBy);
        public const string Owns           = nameof(Owns);

        public const string ContributedTo  = nameof(ContributedTo);
        public const string Contributor    = nameof(Contributor);

        public const string OpenedIssue    = nameof(OpenedIssue);
        public const string OpenedBy       = nameof(OpenedBy);

        public const string IssueOf        = nameof(IssueOf);
        public const string HasIssue       = nameof(HasIssue);

        public const string OpenedPR       = nameof(OpenedPR);
        public const string OpenedPRBy     = nameof(OpenedPRBy);

        public const string PrOf           = nameof(PrOf);
        public const string HasPullRequest = nameof(HasPullRequest);

        public const string ReviewedBy     = nameof(ReviewedBy);
        public const string Reviewed       = nameof(Reviewed);

        public const string OnPullRequest  = nameof(OnPullRequest);
        public const string HasReview      = nameof(HasReview);

        public const string LabeledWith    = nameof(LabeledWith);
        public const string LabelOf        = nameof(LabelOf);

        public const string CoversTopic    = nameof(CoversTopic);
        public const string CoveredByRepo  = nameof(CoveredByRepo);
    }
}
