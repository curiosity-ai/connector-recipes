using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic JSON reader. Works for either a top-level JSON
// array (LoadArray) or a single JSON object per file (LoadObject).
// Reuse as-is for any JSON source — only the model classes need to change.
public static class JsonSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<T> LoadArray<T>(string path)
        => JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Options) ?? new List<T>();

    public static T? LoadObject<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
}
