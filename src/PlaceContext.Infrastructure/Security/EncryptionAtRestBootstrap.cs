using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Security;

/// <summary>
/// One-shot at Host launch: encrypt any legacy plaintext still sitting in Postgres. Idempotent —
/// already-protected values (wire prefix <c>pcenc1.</c>) and empty cells are skipped. Uses
/// <c>IgnoreQueryFilters</c> so every tenant is covered.
/// </summary>
public static class EncryptionAtRestBootstrap
{
    private const string Prefix = DataProtectionEncryptor.Prefix;
    private const int ProjectDataBatch = 200;
    private const int ChainRunBatch = 20;

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EncryptionAtRestBootstrap");
        var enc = sp.GetRequiredService<IDataEncryptor>();
        var db = sp.GetRequiredService<AppDbContext>();
        var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        log.LogInformation("Encryption-at-rest bootstrap: scanning for legacy plaintext…");

        var n = 0;
        n += await EncryptJobColumnsAsync(db, enc, ct);
        n += await EncryptJobRunColumnsAsync(db, enc, ct);
        n += await EncryptRequirementsAsync(db, enc, ct);
        n += await EncryptEventPayloadsAsync(db, enc, ct);
        n += await EncryptDecisionsAsync(db, enc, ct);
        n += await EncryptActivityAsync(db, enc, ct);
        n += await EncryptChartsAsync(db, enc, ct);
        n += await EncryptToolCallsAsync(db, enc, ct);
        n += await EncryptPendingRunsAsync(db, enc, ct);
        n += await EncryptChainRunsAsync(db, enc, ct);
        n += await EncryptVaultIfLegacyAsync(db, enc, ct);
        n += await EncryptProjectDataTextCellsAsync(config, enc, log, ct);

        if (n > 0)
            log.LogInformation("Encryption-at-rest bootstrap: rewrote {Count} field(s)/row(s).", n);
        else
            log.LogInformation("Encryption-at-rest bootstrap: nothing to rewrite (already encrypted or empty).");
    }

    private static bool NeedsProtect(IDataEncryptor enc, string? value)
        => !string.IsNullOrEmpty(value) && !enc.IsProtected(value);

    private static async Task<int> EncryptChainRunsAsync(
        AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.ChainRun;
        var n = 0;
        while (true)
        {
            var rows = await db.ChainRuns.IgnoreQueryFilters()
                .Where(r => (r.StepsJson != "" && !r.StepsJson.StartsWith(Prefix))
                    || (r.FinalOutput != null && r.FinalOutput != ""
                        && !r.FinalOutput.StartsWith(Prefix)))
                .Take(ChainRunBatch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                if (NeedsProtect(enc, row.StepsJson)) { row.StepsJson = enc.Protect(row.StepsJson, p); n++; }
                if (NeedsProtect(enc, row.FinalOutput)) { row.FinalOutput = enc.Protect(row.FinalOutput, p); n++; }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return n;
    }

    private static async Task<int> EncryptJobColumnsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.JobSource;
        var rows = await db.Jobs.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.MapSource)) { r.MapSource = enc.Protect(r.MapSource, p); n++; }
            if (NeedsProtect(enc, r.MapFilesJson)) { r.MapFilesJson = enc.Protect(r.MapFilesJson, p); n++; }
            if (NeedsProtect(enc, r.InputPayloadsJson)) { r.InputPayloadsJson = enc.Protect(r.InputPayloadsJson, p); n++; }
            if (NeedsProtect(enc, r.MapEnvJson)) { r.MapEnvJson = enc.Protect(r.MapEnvJson, p); n++; }
            if (NeedsProtect(enc, r.ReduceSource)) { r.ReduceSource = enc.Protect(r.ReduceSource, p); n++; }
            if (NeedsProtect(enc, r.ReduceFilesJson)) { r.ReduceFilesJson = enc.Protect(r.ReduceFilesJson, p); n++; }
            if (NeedsProtect(enc, r.ReduceEnvJson)) { r.ReduceEnvJson = enc.Protect(r.ReduceEnvJson, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptJobRunColumnsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.JobRun;
        var rows = await db.JobRuns.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.ShardResultsJson)) { r.ShardResultsJson = enc.Protect(r.ShardResultsJson, p); n++; }
            if (NeedsProtect(enc, r.ReduceResultJson)) { r.ReduceResultJson = enc.Protect(r.ReduceResultJson, p); n++; }
            if (NeedsProtect(enc, r.SnapshotJson)) { r.SnapshotJson = enc.Protect(r.SnapshotJson, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptRequirementsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.Requirements;
        var rows = await db.Requirements.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Markdown)) { r.Markdown = enc.Protect(r.Markdown, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptEventPayloadsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.EventPayload;
        var rows = await db.EventOccurrences.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Payload)) { r.Payload = enc.Protect(r.Payload, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptDecisionsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.Decision;
        var rows = await db.Decisions.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Question)) { r.Question = enc.Protect(r.Question, p); n++; }
            if (NeedsProtect(enc, r.Choice)) { r.Choice = enc.Protect(r.Choice, p); n++; }
            if (NeedsProtect(enc, r.Rationale)) { r.Rationale = enc.Protect(r.Rationale, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptActivityAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.Activity;
        var rows = await db.ActivityRecords.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Summary)) { r.Summary = enc.Protect(r.Summary, p); n++; }
            if (NeedsProtect(enc, r.Rationale)) { r.Rationale = enc.Protect(r.Rationale, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptChartsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.Chart;
        var rows = await db.ProjectCharts.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Html)) { r.Html = enc.Protect(r.Html, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptToolCallsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.ToolCall;
        var rows = await db.ToolCalls.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Summary)) { r.Summary = enc.Protect(r.Summary, p); n++; }
            if (NeedsProtect(enc, r.RequestJson)) { r.RequestJson = enc.Protect(r.RequestJson, p); n++; }
            if (NeedsProtect(enc, r.ResponseJson)) { r.ResponseJson = enc.Protect(r.ResponseJson, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptPendingRunsAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.PendingRun;
        var rows = await db.PendingRuns.ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Payload)) { r.Payload = enc.Protect(r.Payload, p); n++; }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static async Task<int> EncryptVaultIfLegacyAsync(AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = IDataEncryptor.Purpose.Vault;
        var rows = await db.JobSecrets.IgnoreQueryFilters().ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            // Leave legacy DP payloads (no pcenc1.) alone — vault historically used DP without our
            // prefix. Only wrap values that look like accidental plaintext.
            if (NeedsProtect(enc, r.Cipher) && LooksLikeAccidentalPlaintext(r.Cipher))
            {
                r.Cipher = enc.Protect(r.Cipher, p);
                n++;
            }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    private static bool LooksLikeAccidentalPlaintext(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 512) return false;
        return s.Any(char.IsWhiteSpace) || s.All(c => char.IsLetterOrDigit(c) || "-_.".Contains(c));
    }

    /// <summary>
    /// Encrypt text/jsonb/varchar cells in every <c>proj_*</c> schema that still lack the wire prefix.
    /// Batched by ctid to bound memory.
    /// </summary>
    private static async Task<int> EncryptProjectDataTextCellsAsync(
        Microsoft.Extensions.Configuration.IConfiguration config,
        IDataEncryptor enc,
        ILogger log,
        CancellationToken ct)
    {
        var cs = config.GetSection("PlaceContext")["ConnectionString"]
            ?? new PlaceContextOptions().ConnectionString;
        var purpose = IDataEncryptor.Purpose.ProjectData;
        var n = 0;

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

        foreach (var schema in schemas)
        {
            var tables = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT c.relname
                    FROM pg_class c
                    JOIN pg_namespace ns ON ns.oid = c.relnamespace
                    WHERE ns.nspname = @s AND c.relkind = 'r'
                    ORDER BY 1
                    """;
                cmd.Parameters.AddWithValue("s", schema);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    tables.Add(r.GetString(0));
            }

            foreach (var table in tables)
            {
                try
                {
                    n += await EncryptTableTextColumnsAsync(conn, enc, purpose, schema, table, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Encryption bootstrap: skip {Schema}.{Table}", schema, table);
                }
            }
        }

        return n;
    }

    private static async Task<int> EncryptTableTextColumnsAsync(
        NpgsqlConnection conn, IDataEncryptor enc, string purpose,
        string schema, string table, CancellationToken ct)
    {
        var qSchema = QuoteIdent(schema);
        var qTable = QuoteIdent(table);

        var columns = new List<(string Name, string Type)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT a.attname, t.typname
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace ns ON ns.oid = c.relnamespace
                JOIN pg_type t ON t.oid = a.atttypid
                WHERE ns.nspname = @s AND c.relname = @t
                  AND a.attnum > 0 AND NOT a.attisdropped
                  AND t.typname IN ('text', 'jsonb', 'varchar')
                ORDER BY a.attnum
                """;
            cmd.Parameters.AddWithValue("s", schema);
            cmd.Parameters.AddWithValue("t", table);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                columns.Add((r.GetString(0), r.GetString(1)));
        }

        var n = 0;
        foreach (var (col, typ) in columns)
            n += await EncryptColumnBatchesAsync(conn, enc, purpose, qSchema, qTable, QuoteIdent(col), typ, ct);
        return n;
    }

    private static async Task<int> EncryptColumnBatchesAsync(
        NpgsqlConnection conn, IDataEncryptor enc, string purpose,
        string qSchema, string qTable, string qCol, string typ, CancellationToken ct)
    {
        var n = 0;
        while (true)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            var batch = new List<(string Ctid, string Value)>();
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = $"""
                    SELECT ctid::text, {qCol}::text
                    FROM {qSchema}.{qTable}
                    WHERE {qCol} IS NOT NULL
                      AND {qCol}::text <> ''
                      AND {qCol}::text NOT LIKE @pfx
                    LIMIT @lim
                    FOR UPDATE
                    """;
                sel.Parameters.AddWithValue("pfx", Prefix + "%");
                sel.Parameters.AddWithValue("lim", ProjectDataBatch);
                await using var r = await sel.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    batch.Add((r.GetString(0), r.GetString(1)));
            }

            if (batch.Count == 0)
            {
                await tx.CommitAsync(ct);
                break;
            }

            foreach (var (ctid, plain) in batch)
            {
                // Skip absurdly large cells rather than OOM the Host.
                if (plain.Length > DataProtectionEncryptor.MaxPlaintextChars) continue;
                // jsonb::text wraps strings in quotes — strip JSON string encoding when present.
                var value = typ == DataColumnTypes.Jsonb ? UnwrapJsonbText(plain) : plain;
                if (enc.IsProtected(value)) continue;
                var cipher = enc.Protect(value, purpose);
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                if (typ == DataColumnTypes.Jsonb)
                    upd.CommandText = $"UPDATE {qSchema}.{qTable} SET {qCol} = to_jsonb(@v::text) WHERE ctid = @ctid::tid";
                else
                    upd.CommandText = $"UPDATE {qSchema}.{qTable} SET {qCol} = @v WHERE ctid = @ctid::tid";
                upd.Parameters.AddWithValue("v", cipher);
                upd.Parameters.AddWithValue("ctid", ctid);
                await upd.ExecuteNonQueryAsync(ct);
                n++;
            }

            await tx.CommitAsync(ct);
        }

        return n;
    }

    /// <summary>Postgres <c>jsonb::text</c> for a JSON string yields quoted JSON; undo that for encryption.</summary>
    private static string UnwrapJsonbText(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(s) ?? s; }
            catch { /* keep raw */ }
        }
        return s;
    }

    private static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}
