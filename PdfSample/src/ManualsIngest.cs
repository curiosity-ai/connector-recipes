using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the sidecar-metadata model, schema registration, and
// the ingestion that turns each PDF + JSON pair into a Manual node plus
// page/chunk/procedure/part/manufacturer/technician/hazard nodes.
public static class ManualsIngest
{
    public sealed class ManualMetadata
    {
        [JsonPropertyName("documentNumber")] public string         DocumentNumber { get; set; } = string.Empty;
        [JsonPropertyName("title")]          public string         Title          { get; set; } = string.Empty;
        [JsonPropertyName("revision")]       public string         Revision       { get; set; } = string.Empty;
        [JsonPropertyName("publishedAt")]    public DateTimeOffset PublishedAt    { get; set; }
        [JsonPropertyName("equipment")]      public EquipmentMeta? Equipment      { get; set; }
        [JsonPropertyName("manufacturer")]   public ManufacturerMeta? Manufacturer { get; set; }
        [JsonPropertyName("authors")]        public List<TechnicianMeta> Authors  { get; set; } = new();
        [JsonPropertyName("procedures")]     public List<ProcedureMeta>  Procedures { get; set; } = new();
        [JsonPropertyName("hazards")]        public List<string>         Hazards    { get; set; } = new();
    }

    public sealed class EquipmentMeta
    {
        [JsonPropertyName("id")]           public string Id           { get; set; } = string.Empty;
        [JsonPropertyName("model")]        public string Model        { get; set; } = string.Empty;
        [JsonPropertyName("category")]     public string Category     { get; set; } = string.Empty;
        [JsonPropertyName("serialNumber")] public string SerialNumber { get; set; } = string.Empty;
        [JsonPropertyName("plant")]        public string Plant        { get; set; } = string.Empty;
    }

    public sealed class ManufacturerMeta
    {
        [JsonPropertyName("name")]    public string Name    { get; set; } = string.Empty;
        [JsonPropertyName("country")] public string Country { get; set; } = string.Empty;
    }

    public sealed class TechnicianMeta
    {
        [JsonPropertyName("employeeId")] public string EmployeeId { get; set; } = string.Empty;
        [JsonPropertyName("name")]       public string Name       { get; set; } = string.Empty;
        [JsonPropertyName("trade")]      public string Trade      { get; set; } = string.Empty;
    }

    public sealed class ProcedureMeta
    {
        [JsonPropertyName("id")]       public string         Id          { get; set; } = string.Empty;
        [JsonPropertyName("name")]     public string         Name        { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string         Category    { get; set; } = string.Empty;
        [JsonPropertyName("severity")] public string         Severity    { get; set; } = string.Empty;
        [JsonPropertyName("page")]     public int            PageNumber  { get; set; }
        [JsonPropertyName("heading")]  public string         Heading     { get; set; } = string.Empty;
        [JsonPropertyName("parts")]    public List<PartMeta> Parts       { get; set; } = new();
        [JsonPropertyName("performedBy")] public string      PerformedBy { get; set; } = string.Empty;
    }

    public sealed class PartMeta
    {
        [JsonPropertyName("partNumber")] public string PartNumber { get; set; } = string.Empty;
        [JsonPropertyName("name")]       public string Name       { get; set; } = string.Empty;
        [JsonPropertyName("category")]   public string Category   { get; set; } = string.Empty;
    }

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Equipment>();
        await graph.CreateNodeSchemaAsync<Nodes.Manual>();
        await graph.CreateNodeSchemaAsync<Nodes.ManualPage>();
        await graph.CreateNodeSchemaAsync<Nodes.TextChunk>();
        await graph.CreateNodeSchemaAsync<Nodes.Procedure>();
        await graph.CreateNodeSchemaAsync<Nodes.Part>();
        await graph.CreateNodeSchemaAsync<Nodes.Manufacturer>();
        await graph.CreateNodeSchemaAsync<Nodes.Technician>();
        await graph.CreateNodeSchemaAsync<Nodes.SafetyHazard>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    public static void Ingest(Graph graph, ExtractedDocument doc, ManualMetadata? meta)
    {
        if (meta is null) return; // a manual without metadata is just a blob; skip

        var manual = graph.AddOrUpdate(new Nodes.Manual
        {
            DocumentNumber = meta.DocumentNumber,
            Title          = meta.Title,
            SourceFile     = doc.SourceFileName,
            ContentHash    = doc.ContentHash,
            Revision       = meta.Revision,
            PageCount      = doc.Pages.Count,
            PublishedAt    = meta.PublishedAt,
        });

        if (meta.Equipment is { Id.Length: > 0 } eq)
        {
            var equipment = graph.AddOrUpdate(new Nodes.Equipment
            {
                Id           = eq.Id,
                Model        = eq.Model,
                Category     = eq.Category,
                SerialNumber = eq.SerialNumber,
                Plant        = eq.Plant,
            });
            graph.Link(equipment, manual, Edges.DocumentedBy, Edges.Documents);

            if (meta.Manufacturer is { Name.Length: > 0 } mf)
            {
                var manufacturer = graph.AddOrUpdate(new Nodes.Manufacturer
                {
                    Name    = mf.Name,
                    Country = mf.Country,
                });
                graph.Link(equipment, manufacturer, Edges.SuppliedBy, Edges.Supplies);
            }
        }

        foreach (var author in meta.Authors)
        {
            if (string.IsNullOrWhiteSpace(author.EmployeeId)) continue;
            var tech = graph.AddOrUpdate(new Nodes.Technician
            {
                EmployeeId = author.EmployeeId,
                Name       = author.Name,
                Trade      = author.Trade,
            });
            graph.Link(manual, tech, Edges.AuthoredBy, Edges.Authored);
        }

        foreach (var hazard in meta.Hazards)
        {
            if (string.IsNullOrWhiteSpace(hazard)) continue;
            var node = graph.TryAdd(new Nodes.SafetyHazard { Name = hazard });
            graph.Link(manual, node, Edges.WarnsAbout, Edges.AppearsIn);
        }

        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var pageNum = i + 1;
            var pageKey = $"{meta.DocumentNumber}#p{pageNum}";
            var page = graph.AddOrUpdate(new Nodes.ManualPage
            {
                Id      = pageKey,
                Number  = pageNum,
                Content = doc.Pages[i],
            });
            graph.Link(manual, page, Edges.HasPage, Edges.PageOf);

            var chunkIdx = 0;
            foreach (var chunk in DocumentSource.Chunk(doc.Pages[i]))
            {
                var chunkKey = $"{pageKey}#c{chunkIdx:000}";
                var chunkNode = graph.AddOrUpdate(new Nodes.TextChunk
                {
                    Id      = chunkKey,
                    PageNum = pageNum,
                    Content = chunk,
                });
                graph.Link(chunkNode, page, Edges.ChunkedFrom, Edges.ChunkOf);
                chunkIdx++;
            }
        }

        foreach (var proc in meta.Procedures)
        {
            if (string.IsNullOrWhiteSpace(proc.Id)) continue;
            var procedure = graph.AddOrUpdate(new Nodes.Procedure
            {
                Id       = proc.Id,
                Name     = proc.Name,
                Category = proc.Category,
                Severity = proc.Severity,
            });
            graph.Link(manual, procedure, Edges.Describes, Edges.DescribedIn);

            if (meta.Equipment is { Id.Length: > 0 } eq2)
                graph.Link(procedure, Node.FromKey(nameof(Nodes.Equipment), eq2.Id), Edges.Maintains, Edges.MaintainedBy);

            if (proc.PageNumber > 0 && proc.PageNumber <= doc.Pages.Count)
            {
                var pageKey = $"{meta.DocumentNumber}#p{proc.PageNumber}";
                graph.Link(procedure, Node.FromKey(nameof(Nodes.ManualPage), pageKey), Edges.DescribedIn, Edges.Describes);
            }

            if (!string.IsNullOrWhiteSpace(proc.PerformedBy))
                graph.Link(procedure, Node.FromKey(nameof(Nodes.Technician), proc.PerformedBy), Edges.PerformedBy, Edges.Performs);

            foreach (var part in proc.Parts)
            {
                if (string.IsNullOrWhiteSpace(part.PartNumber)) continue;
                var partNode = graph.AddOrUpdate(new Nodes.Part
                {
                    PartNumber = part.PartNumber,
                    Name       = part.Name,
                    Category   = part.Category,
                });
                graph.Link(procedure, partNode, Edges.RequiresPart, Edges.UsedIn);
            }
        }
    }
}
