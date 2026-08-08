using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application.Shared;
using NpgsqlTypes;
using PlaceContext.Application.Features;
using PlaceContext.Data.Infrastructure.Persistence;

namespace PlaceContext.Data.Infrastructure.ProjectData;

/// <summary>
/// One-shot at Host launch: flattens JSON already sitting in the project databases. Before the
/// data map flattened at ingest time, an object-valued field landed as one jsonb blob column —
/// this walks every platform-owned (system/read-only) table in every <c>proj_*</c> schema, and
/// for each jsonb column whose rows contain objects adds the same leaf columns ingestion would
/// have produced (<c>meta.region</c> → <c>meta_region</c>, types inferred column-wide) and
/// backfills them. Arrays and scalars are left alone by design (they stay in the blob); the
/// original jsonb column is never dropped. Idempotent — fully flattened rows are skipped — and
/// entirely best-effort: a column that fails is logged and skipped, never fatal to startup.
/// </summary>
public static class JsonFlatteningBootstrap
{
    private const int BatchSize = 500;

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("JsonFlatteningBootstrap");
        var config = sp.GetRequiredService<IConfiguration>();
        var cs = config.GetConnectionString("Data")
            ?? config[$"{DataPersistenceOptions.SectionName}:ConnectionString"]
            ?? config["PlaceContext:ConnectionString"]
            ?? DataPersistenceOptions.DefaultConnectionString;

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        var schemas = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT nspname FROM pg_namespace WHERE nspname LIKE 'proj_%' ORDER BY 1";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                schemas.Add(r.GetString(0));
        }

        var total = 0;
        foreach (var schema in schemas)
        {
            // System tables only: the platform owns them (the project role holds SELECT at most).
            // Project-owned tables are the user's own schema — never restructured for them.
            var tables = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT c.relname
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = @s AND c.relkind = 'r' AND pg_get_userbyid(c.relowner) <> n.nspname
                    ORDER BY 1
                    """;
                cmd.Parameters.AddWithValue("s", schema);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    tables.Add(r.GetString(0));
            }

            foreach (var table in tables)
            {
                var jsonColumns = new List<string>();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT a.attname
                        FROM pg_attribute a
                        JOIN pg_class c ON c.oid = a.attrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        JOIN pg_type t ON t.oid = a.atttypid
                        WHERE n.nspname = @s AND c.relname = @t
                          AND a.attnum > 0 AND NOT a.attisdropped AND t.typname = 'jsonb'
                        ORDER BY a.attnum
                        """;
                    cmd.Parameters.AddWithValue("s", schema);
                    cmd.Parameters.AddWithValue("t", table);
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct))
                        jsonColumns.Add(r.GetString(0));
                }

                foreach (var column in jsonColumns)
                {
                    try
                    {
                        total += await FlattenColumnAsync(conn, log, schema, table, column, ct);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Data-map flattening: skipping {Schema}.{Table}.{Column}.", schema, table, column);
                    }
                }
            }
        }

        if (total > 0)
            log.LogInformation("Data-map flattening: backfilled {Count} row(s) into flattened columns.", total);
        else
            log.LogInformation("Data-map flattening: nothing to backfill (no JSON blobs found, or already flattened).");
    }

    /// <summary>
    /// Flatten one jsonb column. Pass 1 discovers every leaf path and infers its column type
    /// across ALL object rows (so a column is created once, with the merged type); pass 2 adds
    /// the columns and rewrites the rows in ctid-batched transactions. Returns rows updated.
    /// </summary>
    private static async Task<int> FlattenColumnAsync(
        NpgsqlConnection conn, ILogger log, string schema, string table, string column, CancellationToken ct)
    {
        var qTable = $"{QuoteIdent(schema)}.{QuoteIdent(table)}";
        var qColumn = QuoteIdent(column);

        // ── Pass 1: discovery — leaf paths + merged type inference over every object row. ──
        var leafOrder = new List<string>();
        var leafKinds = new Dictionary<string, string?>(StringComparer.Ordinal);
        var last = "(0,0)";
        while (true)
        {
            var found = false;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT ctid::text, {qColumn}::text FROM {qTable}
                    WHERE ctid > @last::tid AND {qColumn} IS NOT NULL AND jsonb_typeof({qColumn}) = 'object'
                    ORDER BY ctid LIMIT @n
                    """;
                cmd.Parameters.AddWithValue("last", last);
                cmd.Parameters.AddWithValue("n", BatchSize);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    found = true;
                    last = r.GetString(0);
                    using var doc = JsonDocument.Parse(r.GetString(1));
                    foreach (var leaf in JsonFlattener.Flatten(doc.RootElement))
                    {
                        var col = JsonFlattener.ColumnName(column, leaf.Path);
                        if (!leafKinds.ContainsKey(col)) leafOrder.Add(col);
                        leafKinds[col] = JsonFlattener.MergeKind(
                            leafKinds.TryGetValue(col, out var k) ? k : null, leaf.Value);
                    }
                }
            }
            if (!found) break;
        }

        // A leaf named like the source column itself (an empty-object root) can't shadow the blob.
        var leaves = leafOrder.Where(c => !string.Equals(c, column, StringComparison.OrdinalIgnoreCase)).ToList();
        if (leaves.Count == 0) return 0;

        // Columns that already exist keep their actual type for the update casts; new ones get the
        // inferred type. (A leaf landing on an existing column writes into it, best-effort.)
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT a.attname, t.typname
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_type t ON t.oid = a.atttypid
                WHERE n.nspname = @s AND c.relname = @t AND a.attnum > 0 AND NOT a.attisdropped
                """;
            cmd.Parameters.AddWithValue("s", schema);
            cmd.Parameters.AddWithValue("t", table);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                existing[r.GetString(0)] = r.GetString(1);
        }

        var adds = leaves.Where(c => !existing.ContainsKey(c))
            .Select(c => $"ADD COLUMN IF NOT EXISTS {QuoteIdent(c)} {JsonFlattener.InferredType(leafKinds[c])}")
            .ToList();
        if (adds.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {qTable} {string.Join(", ", adds)}";
            await cmd.ExecuteNonQueryAsync(ct);
            log.LogInformation("Data-map flattening: {Schema}.{Table}: added column(s) {Columns} from '{SourceColumn}'.",
                schema, table, string.Join(", ", leaves.Where(c => !existing.ContainsKey(c))), column);
        }

        // ── Pass 2: backfill — batched UPDATE by ctid; rows whose leaves are all populated skip. ──
        var castType = leaves.ToDictionary(c => c,
            c => existing.TryGetValue(c, out var typ) ? CastType(typ) : JsonFlattener.InferredType(leafKinds[c]),
            StringComparer.Ordinal);
        var notYetFlattened = string.Join(" OR ", leaves.Select(c => $"{QuoteIdent(c)} IS NULL"));

        var updated = 0;
        last = "(0,0)";
        while (true)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            var batch = new List<(string Ctid, string Json)>();
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = $"""
                    SELECT ctid::text, {qColumn}::text FROM {qTable}
                    WHERE ctid > @last::tid AND {qColumn} IS NOT NULL AND jsonb_typeof({qColumn}) = 'object'
                      AND ({notYetFlattened})
                    ORDER BY ctid LIMIT @n
                    FOR UPDATE
                    """;
                sel.Parameters.AddWithValue("last", last);
                sel.Parameters.AddWithValue("n", BatchSize);
                await using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    batch.Add((r.GetString(0), r.GetString(1)));
            }

            if (batch.Count == 0)
            {
                await tx.CommitAsync(ct);
                break;
            }
            last = batch[^1].Ctid;

            foreach (var (ctid, json) in batch)
            {
                using var doc = JsonDocument.Parse(json);
                var sets = new List<string>();
                var values = new List<(string Param, string Type, string Text)>();
                foreach (var leaf in JsonFlattener.Flatten(doc.RootElement))
                {
                    var col = JsonFlattener.ColumnName(column, leaf.Path);
                    if (!castType.TryGetValue(col, out var type)) continue; // the shadowing leaf — skip
                    var text = JsonFlattener.ValueText(leaf.Value);
                    if (text is null) continue; // absent/JSON-null keys leave the column NULL
                    sets.Add($"{QuoteIdent(col)} = @p{values.Count}::{type}");
                    values.Add(($"p{values.Count}", type, text));
                }
                if (sets.Count == 0) continue;
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"UPDATE {qTable} SET {string.Join(", ", sets)} WHERE ctid = @ctid::tid";
                foreach (var (param, _, text) in values)
                    upd.Parameters.Add(new NpgsqlParameter(param, NpgsqlDbType.Text) { Value = text });
                upd.Parameters.AddWithValue("ctid", ctid);
                await upd.ExecuteNonQueryAsync(ct);
                updated++;
            }

            await tx.CommitAsync(ct);
        }

        return updated;
    }

    // An existing column keeps its real type for the update cast; map the pg_type name onto the
    // store's allowed-type vocabulary (anything exotic degrades to text, which any value casts to).
    private static string CastType(string pgTypeName) => pgTypeName switch
    {
        "bool" => DataColumnTypes.Boolean,
        "int4" => DataColumnTypes.Integer,
        "int8" => DataColumnTypes.Bigint,
        "numeric" or "float4" or "float8" => DataColumnTypes.Numeric,
        "uuid" => DataColumnTypes.Uuid,
        "timestamptz" or "timestamp" => DataColumnTypes.Timestamptz,
        "date" => DataColumnTypes.Date,
        "jsonb" or "json" => DataColumnTypes.Jsonb,
        _ => DataColumnTypes.Text,
    };

    private static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}
