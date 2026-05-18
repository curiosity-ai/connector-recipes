using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Curiosity.Library.Recipes;

// Generic, dataset-agnostic SQLite helper. EnsureSeeded bootstraps a DB from
// a .sql script (useful for self-contained samples). Query<T> runs a SELECT
// and maps each row through the provided projection. Reuse as-is for any
// SQLite source — only the SQL strings and row projections need to change.
//
// Swap for PostgreSQL: replace `Microsoft.Data.Sqlite` with `Npgsql`,
// `SqliteConnection` with `NpgsqlConnection`, drop `EnsureSeeded`, and adjust
// the connection string. The Query<T> shape is unchanged.
public sealed class SqliteSource
{
    private readonly string _connectionString;

    public SqliteSource(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public static void EnsureSeeded(string dbPath, string seedSqlPath)
    {
        if (File.Exists(dbPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = File.ReadAllText(seedSqlPath);
        cmd.ExecuteNonQuery();
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();

        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
