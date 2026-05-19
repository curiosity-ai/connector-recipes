using System;

namespace Curiosity.Library.Recipes;

// Dataset-specific schema. Student.Id and Course.Code match the CSV and
// REST API samples so enrollment events update the same students and
// courses already in the graph; new EnrollmentStatus nodes capture
// in-flight state changes from the CDC stream.
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
        public class Term
        {
            [Key] public string Name { get; set; } = string.Empty;
        }

        // Composite key — one enrollment per (student, course, term).
        // Property values are mutated by subsequent events (waitlist →
        // enrolled → dropped) so re-running the stream from offset 0 lands
        // on the same final state.
        [Node]
        public class Enrollment
        {
            [Key]       public string         Id        { get; set; } = string.Empty;
            [Property]  public string         Status    { get; set; } = string.Empty;
            [Property]  public string         Grade     { get; set; } = string.Empty;
            [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
        }
    }

    public static class Edges
    {
        public const string EnrolledIn    = nameof(EnrolledIn);
        public const string HasEnrollment = nameof(HasEnrollment);

        public const string ForCourse     = nameof(ForCourse);
        public const string CourseOf      = nameof(CourseOf);

        public const string DuringTerm    = nameof(DuringTerm);
        public const string TermOf        = nameof(TermOf);
    }
}
