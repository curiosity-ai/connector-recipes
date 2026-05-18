using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the document model for skills.json, schema registration,
// and the two-pass ingestion that wires up skill→skill edges by key.
public static class SkillsIngest
{
    public sealed class SkillDoc
    {
        [JsonPropertyName("name")]           public string         Name           { get; set; } = string.Empty;
        [JsonPropertyName("category")]       public string         Category       { get; set; } = string.Empty;
        [JsonPropertyName("description")]    public string         Description    { get; set; } = string.Empty;
        [JsonPropertyName("popularity")]     public double         Popularity     { get; set; }
        [JsonPropertyName("yearIntroduced")] public int            YearIntroduced { get; set; }
        [JsonPropertyName("prerequisites")]  public List<string>   Prerequisites  { get; set; } = new();
        [JsonPropertyName("related")]        public List<string>   Related        { get; set; } = new();
        [JsonPropertyName("resources")]      public List<ResourceDoc> Resources   { get; set; } = new();
    }

    public sealed class ResourceDoc
    {
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("url")]   public string Url   { get; set; } = string.Empty;
        [JsonPropertyName("kind")]  public string Kind  { get; set; } = string.Empty;
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Skill>();
        await graph.CreateNodeSchemaAsync<Nodes.SkillCategory>();
        await graph.CreateNodeSchemaAsync<Nodes.LearningResource>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, IReadOnlyList<SkillDoc> skills)
    {
        // Pass 1 — emit every node and its direct edges.
        foreach (var s in skills)
        {
            var skill = graph.AddOrUpdate(new Nodes.Skill
            {
                Name           = s.Name,
                Description    = s.Description,
                Popularity     = s.Popularity,
                YearIntroduced = s.YearIntroduced,
            });

            if (!string.IsNullOrWhiteSpace(s.Category))
            {
                var category = graph.TryAdd(new Nodes.SkillCategory { Name = s.Category });
                graph.Link(skill, category, Edges.HasCategory, Edges.CategoryOf);
            }

            foreach (var r in s.Resources)
            {
                if (string.IsNullOrWhiteSpace(r.Url)) continue;
                var resource = graph.AddOrUpdate(new Nodes.LearningResource
                {
                    Url   = r.Url,
                    Title = r.Title,
                    Kind  = r.Kind,
                });
                graph.Link(resource, skill, Edges.Teaches, Edges.TaughtBy);
            }
        }

        // Pass 2 — wire skill→skill edges by key so order in the file doesn't matter.
        foreach (var s in skills)
        {
            var src = Node.FromKey(nameof(Nodes.Skill), s.Name);

            foreach (var prereq in s.Prerequisites)
                graph.Link(src, Node.FromKey(nameof(Nodes.Skill), prereq), Edges.RequiresSkill, Edges.RequiredBy);

            foreach (var related in s.Related)
                graph.Link(src, Node.FromKey(nameof(Nodes.Skill), related), Edges.RelatedToSkill, Edges.RelatedToSkill);
        }
    }
}
