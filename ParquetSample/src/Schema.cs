namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Student.Id, Subject.Name, and Term.Name match
// the CSV / S3 / REST API samples so grades attach to the same students,
// subjects, and terms already in the graph. Course.Code matches the REST
// API / Kafka samples.
public static class Schema
{
    public static class Nodes
    {
        [Node]
        public class Student
        {
            [Key] public string Id { get; set; } = string.Empty;
        }

        [Node]
        public class Course
        {
            [Key] public string Code { get; set; } = string.Empty;
        }

        [Node]
        public class Subject
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        [Node]
        public class Term
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Composite key — one row in the columnar source per (student,
        // course, term).  Letter grade is the primary property; numeric
        // grade and credit hours roll up nicely under aggregation queries.
        [Node]
        public class Grade
        {
            [Key]      public string Id          { get; set; } = string.Empty;
            [Property] public string Letter      { get; set; } = string.Empty;
            [Property] public double GpaPoints   { get; set; }
            [Property] public int    CreditHours { get; set; }
        }
    }

    public static class Edges
    {
        public const string Received    = nameof(Received);
        public const string ReceivedBy  = nameof(ReceivedBy);

        public const string ForCourse   = nameof(ForCourse);
        public const string CourseOf    = nameof(CourseOf);

        public const string CoversSubject = nameof(CoversSubject);
        public const string CoveredBy     = nameof(CoveredBy);

        public const string DuringTerm  = nameof(DuringTerm);
        public const string TermOf      = nameof(TermOf);
    }
}
