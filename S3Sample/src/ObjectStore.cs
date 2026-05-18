using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic object store. The same `(Key, Content)` stream
// is produced whether the backing store is a local directory or an S3 bucket,
// so ingestion code is identical in both modes. Reuse as-is for any
// document-per-object source.
public interface IObjectStore
{
    IAsyncEnumerable<(string Key, string Content)> ListAsync(string prefix);
}

public sealed class LocalObjectStore : IObjectStore
{
    private readonly string _root;

    public LocalObjectStore(string root) { _root = root; }

    public async IAsyncEnumerable<(string Key, string Content)> ListAsync(string prefix)
    {
        var dir = Path.Combine(_root, prefix);
        if (!Directory.Exists(dir)) yield break;

        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            var key  = Path.GetRelativePath(_root, path).Replace('\\', '/');
            var body = await File.ReadAllTextAsync(path);
            yield return (key, body);
        }
    }
}

public sealed class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly string    _bucket;

    public S3ObjectStore(IAmazonS3 client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    public async IAsyncEnumerable<(string Key, string Content)> ListAsync(string prefix)
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

                using var get  = await _client.GetObjectAsync(_bucket, obj.Key);
                using var sr   = new StreamReader(get.ResponseStream);
                var       body = await sr.ReadToEndAsync();
                yield return (obj.Key, body);
            }

            continuationToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }
}
