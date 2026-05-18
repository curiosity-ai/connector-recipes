using System;
using Curiosity.Library;

namespace ConnectorRecipes.Csv;

public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Student
        {
            [Key]      public string Id              { get; set; } = string.Empty;
            [Property] public string Name            { get; set; } = string.Empty;
            [Property] public int    BirthYear       { get; set; }
            [Property] public int    EnrollmentYear  { get; set; }
            [Property] public double Gpa             { get; set; }
        }

        [Node]
        public class University
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Composite key "<University>/<Department>" so the same department
        // name (e.g. "Computer Science") at different universities is distinct.
        [Node]
        public class Department
        {
            [Key]      public string Id   { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Degree
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Major
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Subject
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Skill
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Advisor
        {
            [Key]      public string Email { get; set; } = string.Empty;
            [Property] public string Name  { get; set; } = string.Empty;
        }

        [Node]
        public class Country
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Composite key "<City>, <Country>" — different cities sharing a name
        // (e.g. Cambridge, USA vs Cambridge, UK) stay separate.
        [Node]
        public class City
        {
            [Key]      public string Id   { get; set; } = string.Empty;
            [Property] public string Name { get; set; } = string.Empty;
        }
    }

    public static class Edges
    {
        public const string EnrolledAt           = nameof(EnrolledAt);
        public const string EnrolledStudent      = nameof(EnrolledStudent);

        public const string BelongsToDepartment  = nameof(BelongsToDepartment);
        public const string HasMember            = nameof(HasMember);

        public const string PursuesDegree        = nameof(PursuesDegree);
        public const string PursuedBy            = nameof(PursuedBy);

        public const string HasMajor             = nameof(HasMajor);
        public const string MajorOf              = nameof(MajorOf);

        public const string Studies              = nameof(Studies);
        public const string StudiedBy            = nameof(StudiedBy);

        public const string HasSkill             = nameof(HasSkill);
        public const string SkillOf              = nameof(SkillOf);

        public const string AdvisedBy            = nameof(AdvisedBy);
        public const string Advises              = nameof(Advises);

        public const string LivesIn              = nameof(LivesIn);
        public const string Resident             = nameof(Resident);

        public const string PartOf               = nameof(PartOf);
        public const string HasDepartment        = nameof(HasDepartment);

        public const string OfferedBy            = nameof(OfferedBy);
        public const string Offers               = nameof(Offers);

        public const string WorksIn              = nameof(WorksIn);
        public const string Employs              = nameof(Employs);

        public const string In                   = nameof(In);
        public const string Includes             = nameof(Includes);
    }
}
