using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static Curiosity.Library.Recipes.Schema;

namespace Curiosity.Library.Recipes;

// Dataset-specific: the grants row model, schema registration, and the
// keyset-paged ingestion driven by an updated_at watermark for incremental
// pulls.
public static class GrantsIngest
{
    public sealed record GrantRow(
        string Id,
        string Title,
        long   AmountUsd,
        int    StartYear,
        int    EndYear,
        string Status,
        DateTimeOffset AwardedAt,
        DateTimeOffset UpdatedAt,
        string PiEmail,
        string PiName,
        string University,
        string ResearchArea,
        string AgencyAcronym,
        string AgencyName,
        string AgencyCountry);

    public static async Task RegisterSchemaAsync(Graph graph)
    {
        await graph.CreateNodeSchemaAsync<Nodes.Grant>();
        await graph.CreateNodeSchemaAsync<Nodes.Faculty>();
        await graph.CreateNodeSchemaAsync<Nodes.University>();
        await graph.CreateNodeSchemaAsync<Nodes.ResearchArea>();
        await graph.CreateNodeSchemaAsync<Nodes.FundingAgency>();
        await graph.CreateEdgeSchemaAsync(typeof(Edges));
    }

    // Single ORDER-BY-watermark query; keyset pagination uses updated_at as
    // the cursor. Re-running with the last successfully ingested updated_at
    // skips already-processed rows — the standard "watermark" pattern.
    public const string PagedSql =
        "SELECT id, title, amount_usd, start_year, end_year, status, " +
        "       awarded_at, updated_at, pi_email, pi_name, university, " +
        "       research_area, agency_acronym, agency_name, agency_country " +
        "FROM grants " +
        "WHERE updated_at > @startKey " +
        "ORDER BY updated_at ASC " +
        "LIMIT @pageSize";

    public static GrantRow Map(DbDataReader r) => new(
        Id:            r.GetString(0),
        Title:         r.GetString(1),
        AmountUsd:     r.GetInt64(2),
        StartYear:     r.GetInt32(3),
        EndYear:       r.GetInt32(4),
        Status:        r.GetString(5),
        AwardedAt:     new DateTimeOffset(r.GetDateTime(6), TimeSpan.Zero),
        UpdatedAt:     new DateTimeOffset(r.GetDateTime(7), TimeSpan.Zero),
        PiEmail:       r.GetString(8),
        PiName:        r.GetString(9),
        University:    r.GetString(10),
        ResearchArea:  r.GetString(11),
        AgencyAcronym: r.GetString(12),
        AgencyName:    r.GetString(13),
        AgencyCountry: r.GetString(14));

    public static void Ingest(Graph graph, GrantRow row)
    {
        var grant = graph.AddOrUpdate(new Nodes.Grant
        {
            Id        = row.Id,
            Title     = row.Title,
            AmountUsd = row.AmountUsd,
            StartYear = row.StartYear,
            EndYear   = row.EndYear,
            Status    = row.Status,
            AwardedAt = row.AwardedAt,
        });

        var pi = graph.AddOrUpdate(new Nodes.Faculty
        {
            Email = row.PiEmail,
            Name  = row.PiName,
        });
        graph.Link(grant, pi, Edges.AwardedTo, Edges.Holds);

        var university = graph.TryAdd(new Nodes.University { Name = row.University });
        graph.Link(pi, university, Edges.AffiliatedWith, Edges.Affiliates);

        var area = graph.TryAdd(new Nodes.ResearchArea { Name = row.ResearchArea });
        graph.Link(grant, area, Edges.Covers, Edges.CoveredBy);

        var agency = graph.AddOrUpdate(new Nodes.FundingAgency
        {
            Acronym = row.AgencyAcronym,
            Name    = row.AgencyName,
            Country = row.AgencyCountry,
        });
        graph.Link(grant, agency, Edges.FundedBy, Edges.Funds);
    }
}
