using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectorRecipes.SkillsJson;

public sealed class SkillJson
{
    [JsonPropertyName("name")]           public string Name           { get; set; } = string.Empty;
    [JsonPropertyName("category")]       public string Category       { get; set; } = string.Empty;
    [JsonPropertyName("description")]    public string Description    { get; set; } = string.Empty;
    [JsonPropertyName("popularity")]     public double Popularity     { get; set; }
    [JsonPropertyName("yearIntroduced")] public int    YearIntroduced { get; set; }
    [JsonPropertyName("prerequisites")]  public List<string> Prerequisites { get; set; } = new();
    [JsonPropertyName("related")]        public List<string> Related       { get; set; } = new();
    [JsonPropertyName("resources")]      public List<ResourceJson> Resources { get; set; } = new();
}

public sealed class ResourceJson
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("url")]   public string Url   { get; set; } = string.Empty;
    [JsonPropertyName("kind")]  public string Kind  { get; set; } = string.Empty;
}

public static class SkillsJsonLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<SkillJson> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<SkillJson>>(json, Options) ?? new List<SkillJson>();
    }
}
