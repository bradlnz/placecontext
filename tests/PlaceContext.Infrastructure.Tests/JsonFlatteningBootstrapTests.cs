using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Infrastructure.ProjectData;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Integration test for <see cref="JsonFlatteningBootstrap"/>: a platform-owned table with a jsonb
/// blob column gets typed leaf columns added and backfilled; array columns are left alone; a second
/// run is a no-op. Skipped by default (consistent with the repo's Docker/Postgres-dependent tests) —
/// run explicitly against the setup.sh dev database.
/// </summary>
public class JsonFlatteningBootstrapTests
{
    private const string Cs = "Host=localhost;Port=5433;Database=placecontext;Username=postgres;Password=postgres";
    private const string Schema = "proj_flattentest";

    [Fact(Skip = "Requires local Postgres on localhost:5433 (the setup.sh dev database)")]
    public async Task Flattens_jsonb_blob_columns_into_typed_leaf_columns()
    {
        await using (var setup = new NpgsqlConnection(Cs))
        {
            await setup.OpenAsync();
            // Plain (non-interpolated) raw string: the JSON braces must stay literal.
            await Exec(setup, """
                DROP SCHEMA IF EXISTS "proj_flattentest" CASCADE;
                CREATE SCHEMA "proj_flattentest";
                CREATE TABLE "proj_flattentest".listings (
                    ingested_at timestamptz NOT NULL,
                    run_id uuid NOT NULL,
                    meta jsonb,
                    tags jsonb
                );
                INSERT INTO "proj_flattentest".listings (ingested_at, run_id, meta, tags) VALUES
                    (now(), gen_random_uuid(), '{"region":"QLD","pop":2500000,"capital":true,"geo":{"lat":-27.4}}', '["a","b"]'),
                    (now(), gen_random_uuid(), '{"region":"NSW","pop":null}', '["c"]');
                """);
        }

        try
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["PlaceContext:ConnectionString"] = Cs })
                    .Build())
                .BuildServiceProvider();

            await JsonFlatteningBootstrap.RunAsync(services);
            await JsonFlatteningBootstrap.RunAsync(services); // idempotent re-run

            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();

            // Leaf columns added with inferred types; the blobs stay.
            var columns = new Dictionary<string, string>();
            await using (var cmd = new NpgsqlCommand(
                """
                SELECT a.attname, t.typname
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_type t ON t.oid = a.atttypid
                WHERE n.nspname = @s AND c.relname = 'listings' AND a.attnum > 0 AND NOT a.attisdropped
                """, conn))
            {
                cmd.Parameters.AddWithValue("s", Schema);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    columns[r.GetString(0)] = r.GetString(1);
            }
            Assert.Equal("text", columns["meta_region"]);
            Assert.Equal("numeric", columns["meta_pop"]);
            Assert.Equal("bool", columns["meta_capital"]);
            Assert.Equal("text", columns["meta_geo_lat"]);
            Assert.False(columns.Keys.Any(c => c.StartsWith("tags_")), "arrays are never flattened");

            var rows = new List<(string? Region, decimal? Pop, bool? Capital, string? Lat, string? Tags)>();
            await using (var cmd = new NpgsqlCommand(
                $"""SELECT meta_region, meta_pop, meta_capital, meta_geo_lat::text, tags::text FROM "{Schema}".listings ORDER BY meta_region""", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    rows.Add((r.IsDBNull(0) ? null : r.GetString(0),
                        r.IsDBNull(1) ? null : r.GetDecimal(1),
                        r.IsDBNull(2) ? null : r.GetBoolean(2),
                        r.IsDBNull(3) ? null : r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4)));
            }
            Assert.Equal(2, rows.Count);
            Assert.Equal(("NSW", null, null, null, """["c"]"""), rows[0]);
            Assert.Equal(("QLD", 2500000m, true, "-27.4", """["a", "b"]"""), rows[1]);
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(Cs);
            await cleanup.OpenAsync();
            await Exec(cleanup, $"""DROP SCHEMA IF EXISTS "{Schema}" CASCADE""");
        }
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
