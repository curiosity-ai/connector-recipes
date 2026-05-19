using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic stream reader. The same async-enumerable of
// typed events is produced whether the backing source is a real Kafka topic
// or a local JSONL file — keeps ingestion code identical in both modes and
// makes the recipe runnable offline.
//
// The Kafka variant exposes the canonical CDC contract:
//   - manual offset commits AFTER a batch commits to the graph (no message
//     loss on crash, at-most-once duplicates on reprocess)
//   - consumer-group-managed offsets persist across restarts (no need for a
//     side-channel watermark)
//   - keyed messages let the graph emit idempotent upserts on shared keys

public sealed record StreamEvent<T>(string Key, T Value, long Offset, int Partition);

public interface IEventStreamSource<T>
{
    IAsyncEnumerable<StreamEvent<T>> ConsumeAsync(CancellationToken ct = default);
    void CommitOffset(StreamEvent<T> evt);
    void Dispose();
}

public sealed class KafkaEventStreamSource<T> : IEventStreamSource<T>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IConsumer<string, string> _consumer;
    private readonly string                    _topic;
    private readonly ILogger?                  _logger;

    public KafkaEventStreamSource(string bootstrapServers, string groupId, string topic, ILogger? logger = null)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers      = bootstrapServers,
            GroupId               = groupId,
            // Resume from committed offset; start at earliest if this is a new group.
            AutoOffsetReset       = AutoOffsetReset.Earliest,
            // Manual commits — we only commit after the graph commit succeeds.
            EnableAutoCommit      = false,
            EnablePartitionEof    = true,
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(topic);
        _topic   = topic;
        _logger  = logger;
    }

    public async IAsyncEnumerable<StreamEvent<T>> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try   { result = _consumer.Consume(TimeSpan.FromSeconds(1)); }
            catch (ConsumeException ex) { _logger?.LogWarning(ex, "Kafka consume error on topic {Topic}", _topic); continue; }

            if (result is null) continue;
            if (result.IsPartitionEOF) continue;

            var value = JsonSerializer.Deserialize<T>(result.Message.Value, Json);
            if (value is null) continue;

            yield return new StreamEvent<T>(
                Key:       result.Message.Key,
                Value:     value,
                Offset:    result.Offset.Value,
                Partition: result.Partition.Value);
        }
    }

    // Mark the message as durably processed. Call AFTER the graph has been
    // committed so an in-flight crash always replays from the last
    // successfully ingested message — at-least-once delivery. Because we
    // upsert on the message key, the second processing of the same message
    // is a no-op at the graph layer; effective behavior is exactly-once-ish.
    public void CommitOffset(StreamEvent<T> evt)
    {
        var tpo = new TopicPartitionOffset(_topic, new Partition(evt.Partition), new Offset(evt.Offset + 1));
        _consumer.StoreOffset(tpo);
        _consumer.Commit();
    }

    public void Dispose()
    {
        try { _consumer.Close(); } catch { }
        _consumer.Dispose();
    }
}

// Local fallback: one JSON document per line in `events.jsonl`. The wrapping
// envelope adds a key + offset so the same ingestion code works without a
// real Kafka cluster. Offsets are committed to a side file so re-running the
// recipe resumes from where it left off — same semantics as the real source.
public sealed class JsonlEventStreamSource<T> : IEventStreamSource<T>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _eventsPath;
    private readonly string _offsetPath;

    public JsonlEventStreamSource(string eventsPath, string offsetPath)
    {
        _eventsPath = eventsPath;
        _offsetPath = offsetPath;
    }

    public async IAsyncEnumerable<StreamEvent<T>> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_eventsPath)) yield break;

        long start = 0;
        if (File.Exists(_offsetPath) && long.TryParse(File.ReadAllText(_offsetPath).Trim(), out var saved))
            start = saved;

        var offset = 0L;
        using var reader = new StreamReader(_eventsPath);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (offset++ < start) continue;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var envelope = JsonSerializer.Deserialize<Envelope>(line, Json);
            if (envelope is null || envelope.Value is null) continue;
            var value = envelope.Value.Value.Deserialize<T>(Json);
            if (value is null) continue;

            yield return new StreamEvent<T>(envelope.Key, value, offset - 1, 0);
        }
    }

    public void CommitOffset(StreamEvent<T> evt)
        => File.WriteAllText(_offsetPath, (evt.Offset + 1).ToString());

    public void Dispose() { }

    private sealed class Envelope
    {
        public string       Key   { get; set; } = string.Empty;
        public JsonElement? Value { get; set; }
    }
}
