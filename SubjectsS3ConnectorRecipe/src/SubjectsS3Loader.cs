using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace ConnectorRecipes.SubjectsS3;

public sealed class SubjectJson
{
    [JsonPropertyName("name")]        public string Name        { get; set; } = string.Empty;
    [JsonPropertyName("level")]       public string Level       { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("topics")]      public List<string> Topics    { get; set; } = new();
    [JsonPropertyName("bookIsbns")]   public List<string> BookIsbns { get; set; } = new();
}

public sealed class BookJson
{
    [JsonPropertyName("isbn")]    public string Isbn    { get; set; } = string.Empty;
    [JsonPropertyName("title")]   public string Title   { get; set; } = string.Empty;
    [JsonPropertyName("year")]    public int    Year    { get; set; }
    [JsonPropertyName("edition")] public int    Edition { get; set; }
    [JsonPropertyName("authors")] public List<string> Authors { get; set; } = new();
}

/// <summary>
/// Reads JSON documents from a prefix, abstracting over local filesystem and S3.
/// Both modes return the same `(Key, Json)` stream so ingestion code is identical.
/// </summary>
public interface ISubjectsSource
{
    IAsyncEnumerable<(string Key, string Json)> ListAsync(string prefix);
}

public sealed class LocalSubjectsSource : ISubjectsSource
{
    private readonly string _root;

    public LocalSubjectsSource(string root) { _root = root; }

    public async IAsyncEnumerable<(string Key, string Json)> ListAsync(string prefix)
    {
        var dir = Path.Combine(_root, prefix);
        if (!Directory.Exists(dir)) yield break;

        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var key  = Path.GetRelativePath(_root, path).Replace('\\', '/');
            var json = await File.ReadAllTextAsync(path);
            yield return (key, json);
        }
    }
}

public sealed class S3SubjectsSource : ISubjectsSource
{
    private readonly IAmazonS3 _client;
    private readonly string    _bucket;

    public S3SubjectsSource(IAmazonS3 client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    public async IAsyncEnumerable<(string Key, string Json)> ListAsync(string prefix)
    {
        string? continuationToken = null;
        do
        {
            var resp = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName        = _bucket,
                Prefix            = prefix,
                ContinuationToken = continuationToken,
            });

            foreach (var obj in resp.S3Objects)
            {
                if (!obj.Key.EndsWith(".json")) continue;

                using var get   = await _client.GetObjectAsync(_bucket, obj.Key);
                using var sr    = new StreamReader(get.ResponseStream);
                var       body  = await sr.ReadToEndAsync();
                yield return (obj.Key, body);
            }

            continuationToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }
}
