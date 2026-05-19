using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic source for PostgreSQL or MySQL servers. The
// connection-string scheme picks the driver; the Query<T> shape is identical
// to the SqliteSource in SqlSample but iterates in batches so very large
// tables don't materialize in memory.
//
// Server-side cursoring: `StreamPaged` uses keyset pagination (WHERE id > ?
// ORDER BY id LIMIT N) — works on any server, avoids the OFFSET trap
// (linear scan with each page), and naturally supports incremental sync by
// passing the previous high-watermark as the starting key.
public enum SqlFlavor { Postgres, MySql }

public sealed class SqlServerSource
{
    private readonly string    _connectionString;
    private readonly SqlFlavor _flavor;
    private readonly ILogger?  _logger;

    public SqlServerSource(string connectionString, SqlFlavor flavor, ILogger? logger = null)
    {
        _connectionString = connectionString;
        _flavor           = flavor;
        _logger           = logger;
    }

    // Auto-detect from common URI prefixes.  postgres://, postgresql:// → Postgres
    //                                        mysql://, mariadb://       → MySql
    public static SqlServerSource FromUrl(string url, ILogger? logger = null)
    {
        var (cs, flavor) = ParseUrl(url);
        return new SqlServerSource(cs, flavor, logger);
    }

    public List<T> Query<T>(string sql, Func<DbDataReader, T> map)
    {
        using var conn = Open();
        using var cmd  = CreateCommand(conn, sql);
        using var r    = cmd.ExecuteReader();

        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    // Keyset pagination by an ordered key column (typically the primary key
    // or a monotonically-increasing watermark like updated_at). The first
    // call passes startKey = the previous run's high watermark; subsequent
    // pages pass the max key from the prior page. Yields rows one batch at
    // a time so memory stays bounded.
    public IEnumerable<T> StreamPaged<T>(
        string sqlTemplate,
        string startKey,
        Func<DbDataReader, T> map,
        Func<T, string> keyOf,
        int pageSize = 1000)
    {
        var cursor = startKey;
        while (true)
        {
            var page = QueryPage<T>(sqlTemplate, cursor, pageSize, map);
            if (page.Count == 0) yield break;

            foreach (var row in page) yield return row;
            if (page.Count < pageSize) yield break;

            cursor = keyOf(page[^1]);
            _logger?.LogDebug("Advancing cursor to {Cursor}", cursor);
        }
    }

    private List<T> QueryPage<T>(string sqlTemplate, string startKey, int pageSize, Func<DbDataReader, T> map)
    {
        using var conn = Open();
        using var cmd  = CreateCommand(conn, sqlTemplate);
        AddParameter(cmd, "@startKey", startKey);
        AddParameter(cmd, "@pageSize", pageSize);
        using var r = cmd.ExecuteReader();

        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private DbConnection Open()
    {
        DbConnection conn = _flavor == SqlFlavor.Postgres
            ? new NpgsqlConnection(_connectionString)
            : new MySqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static DbCommand CreateCommand(DbConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value         = value;
        cmd.Parameters.Add(p);
    }

    private static (string ConnectionString, SqlFlavor Flavor) ParseUrl(string url)
    {
        if (url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            var userInfo = uri.UserInfo.Split(':', 2);
            var db   = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;
            var cs = $"Host={uri.Host};Port={port};Database={db};Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};";
            return (cs, SqlFlavor.Postgres);
        }
        if (url.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mariadb://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            var userInfo = uri.UserInfo.Split(':', 2);
            var db   = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 3306;
            var cs = $"Server={uri.Host};Port={port};Database={db};User Id={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};";
            return (cs, SqlFlavor.MySql);
        }
        throw new ArgumentException($"Unrecognized SQL URL scheme: {url}");
    }

    // Tracks the last successfully ingested key, persisted to disk so the
    // next run resumes where this one left off.  Trivial JSON-line store
    // keeps it dependency-free; swap for a workspace metadata node in
    // production if you want the watermark to live with the graph.
    public sealed class Watermark
    {
        private readonly string _path;

        public Watermark(string path) { _path = path; }

        public string Read(string fallback)
            => File.Exists(_path) ? File.ReadAllText(_path).Trim() : fallback;

        public void Write(string value)
            => File.WriteAllText(_path, value);
    }
}
