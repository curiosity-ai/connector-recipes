using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the document model for the `profiles` collection, schema
// registration, and the ingestion that projects nested arrays (internships,
// projects, interests) into typed nodes and edges.
public static class ProfilesIngest
{
    public sealed class ProfileDoc
    {
        [JsonPropertyName("studentId")] public string                StudentId   { get; set; } = string.Empty;
        [JsonPropertyName("bio")]       public string                Bio         { get; set; } = string.Empty;
        [JsonPropertyName("githubUrl")] public string                GithubUrl   { get; set; } = string.Empty;
        [JsonPropertyName("linkedin")]  public string                LinkedIn    { get; set; } = string.Empty;
        [JsonPropertyName("skills")]    public List<string>          Skills      { get; set; } = new();
        [JsonPropertyName("interests")] public List<string>          Interests   { get; set; } = new();
        [JsonPropertyName("internships")] public List<InternshipDoc> Internships { get; set; } = new();
        [JsonPropertyName("projects")]    public List<ProjectDoc>    Projects    { get; set; } = new();
    }

    public sealed class InternshipDoc
    {
        [JsonPropertyName("id")]        public string CompanyAndYear { get; set; } = string.Empty;
        [JsonPropertyName("company")]   public string Company        { get; set; } = string.Empty;
        [JsonPropertyName("industry")]  public string Industry       { get; set; } = string.Empty;
        [JsonPropertyName("website")]   public string Website        { get; set; } = string.Empty;
        [JsonPropertyName("role")]      public string Role           { get; set; } = string.Empty;
        [JsonPropertyName("startYear")] public int    StartYear      { get; set; }
        [JsonPropertyName("endYear")]   public int    EndYear        { get; set; }
    }

    public sealed class ProjectDoc
    {
        [JsonPropertyName("id")]          public string       Id          { get; set; } = string.Empty;
        [JsonPropertyName("name")]        public string       Name        { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string       Description { get; set; } = string.Empty;
        [JsonPropertyName("url")]         public string       Url         { get; set; } = string.Empty;
        [JsonPropertyName("skills")]      public List<string> Skills      { get; set; } = new();
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Student>();
        await graph.CreateNodeSchemaAsync<Nodes.Skill>();
        await graph.CreateNodeSchemaAsync<Nodes.Internship>();
        await graph.CreateNodeSchemaAsync<Nodes.Company>();
        await graph.CreateNodeSchemaAsync<Nodes.Project>();
        await graph.CreateNodeSchemaAsync<Nodes.Interest>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, ProfileDoc doc)
    {
        var student = graph.AddOrUpdate(new Nodes.Student
        {
            Id        = doc.StudentId,
            Bio       = doc.Bio,
            GithubUrl = doc.GithubUrl,
            LinkedIn  = doc.LinkedIn,
        });

        foreach (var skillName in doc.Skills)
        {
            var skill = graph.TryAdd(new Nodes.Skill { Name = skillName });
            graph.Link(student, skill, Edges.Uses, Edges.UsedIn);
        }

        foreach (var interest in doc.Interests)
        {
            var node = graph.TryAdd(new Nodes.Interest { Name = interest });
            graph.Link(student, node, Edges.InterestedIn, Edges.InterestOf);
        }

        foreach (var ship in doc.Internships)
        {
            if (string.IsNullOrWhiteSpace(ship.Company)) continue;
            var company = graph.AddOrUpdate(new Nodes.Company
            {
                Name     = ship.Company,
                Industry = ship.Industry,
                Website  = ship.Website,
            });
            var key = string.IsNullOrEmpty(ship.CompanyAndYear)
                ? $"{doc.StudentId}/{ship.Company}/{ship.StartYear}"
                : ship.CompanyAndYear;
            var internship = graph.AddOrUpdate(new Nodes.Internship
            {
                Id        = key,
                Role      = ship.Role,
                StartYear = ship.StartYear,
                EndYear   = ship.EndYear,
            });
            graph.Link(student,    internship, Edges.Held, Edges.HeldBy);
            graph.Link(internship, company,    Edges.At,   Edges.Hosted);
        }

        foreach (var project in doc.Projects)
        {
            var key = string.IsNullOrEmpty(project.Id) ? $"{doc.StudentId}/{project.Name}" : project.Id;
            var projectNode = graph.AddOrUpdate(new Nodes.Project
            {
                Id          = key,
                Name        = project.Name,
                Description = project.Description,
                Url         = project.Url,
            });
            graph.Link(student, projectNode, Edges.Built, Edges.BuiltBy);

            foreach (var skillName in project.Skills)
            {
                var skill = graph.TryAdd(new Nodes.Skill { Name = skillName });
                graph.Link(projectNode, skill, Edges.Uses, Edges.UsedIn);
            }
        }
    }
}
