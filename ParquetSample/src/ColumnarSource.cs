using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avro.File;
using Avro.Generic;
using Parquet;
using Parquet.Schema;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic columnar reader. Reads one row group (Parquet)
// or one block (Avro) at a time, projects only the columns the consumer
// asks for, and yields strongly-typed rows. The point is to stay row-group
// bounded in memory — never materialize a whole file even when it's millions
// of rows — and to skip columns the ingestion doesn't need.
//
// Reuse as-is for any columnar-on-the-lake source. Parquet covers most
// cases; Avro is included for the second common dialect.
public sealed record ColumnarRow(IReadOnlyDictionary<string, object?> Values)
{
    public T? Get<T>(string name)
    {
        if (!Values.TryGetValue(name, out var v) || v is null) return default;
        if (v is T typed) return typed;
        return (T)Convert.ChangeType(v, typeof(T))!;
    }
}

public interface IColumnarSource
{
    IAsyncEnumerable<ColumnarRow> ReadAsync(string path, IReadOnlyList<string>? columns = null);
}

public sealed class ParquetSource : IColumnarSource
{
    public async IAsyncEnumerable<ColumnarRow> ReadAsync(string path, IReadOnlyList<string>? columns = null)
    {
        await using var stream = File.OpenRead(path);
        using var       reader = await ParquetReader.CreateAsync(stream);

        var fields = reader.Schema.DataFields;
        var picked = columns is null or { Count: 0 }
            ? (IReadOnlyList<DataField>)fields
            : fields.Where(f => columns.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).ToList();

        for (var groupIdx = 0; groupIdx < reader.RowGroupCount; groupIdx++)
        {
            using var group   = reader.OpenRowGroupReader(groupIdx);
            var columnsRead   = new Dictionary<string, Array>(picked.Count, StringComparer.OrdinalIgnoreCase);
            var rowCount      = (int)group.RowCount;

            foreach (var field in picked)
            {
                var col = await group.ReadColumnAsync(field);
                columnsRead[field.Name] = col.Data;
            }

            for (var i = 0; i < rowCount; i++)
            {
                var values = new Dictionary<string, object?>(picked.Count);
                foreach (var (name, arr) in columnsRead)
                    values[name] = arr.GetValue(i);
                yield return new ColumnarRow(values);
            }
        }
    }
}

public sealed class AvroSource : IColumnarSource
{
#pragma warning disable CS1998 // sync file reader inside async iterator; intentional
    public async IAsyncEnumerable<ColumnarRow> ReadAsync(string path, IReadOnlyList<string>? columns = null)
    {
        using var stream = File.OpenRead(path);
        using var reader = DataFileReader<GenericRecord>.OpenReader(stream);

        var schema = reader.GetSchema() as Avro.RecordSchema;
        var fields = schema?.Fields ?? new List<Avro.Field>();
        var picked = columns is null or { Count: 0 }
            ? fields.Select(f => f.Name).ToList()
            : fields.Where(f => columns.Contains(f.Name, StringComparer.OrdinalIgnoreCase)).Select(f => f.Name).ToList();

        while (reader.HasNext())
        {
            var record = reader.Next();
            var values = new Dictionary<string, object?>(picked.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var name in picked)
            {
                if (record.TryGetValue(name, out var v)) values[name] = v;
            }
            yield return new ColumnarRow(values);
        }
    }
#pragma warning restore CS1998
}
