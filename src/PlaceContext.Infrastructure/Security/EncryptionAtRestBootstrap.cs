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
    private const int CrmBatch = 200;
    private const int CrmPayloadBatch = 20;

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
        n += await EncryptChainRunsAsync(db, enc, crmOnly: false, ct);
        n += await EncryptCrmClientsAsync(db, enc, ct);
        n += await EncryptCrmCommunicationsAsync(db, enc, ct);
        n += await EncryptCrmArtifactMetadataAsync(db, enc, ct);
        n += await EncryptCrmAutomationErrorsAsync(db, enc, ct);
        n += await EncryptProjectDataTextCellsAsync(config, enc, log, ct);

        if (n > 0)
            log.LogInformation("Encryption-at-rest bootstrap: rewrote {Count} field(s)/row(s).", n);
        else
            log.LogInformation("Encryption-at-rest bootstrap: nothing to rewrite (already encrypted or empty).");
    }

    /// <summary>
    /// Lightweight startup backfill for CRM customer data. Unlike the opt-in historical bootstrap,
    /// this is safe to run before every launch: it reads at most <see cref="CrmBatch"/> small rows at
    /// a time and only selects legacy plaintext. This guarantees that pre-encryption CRM rows do not
    /// remain readable in a database snapshot after an upgrade.
    /// </summary>
    public static async Task RunCrmAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CrmEncryptionAtRestBootstrap");
        var enc = sp.GetRequiredService<IDataEncryptor>();
        var db = sp.GetRequiredService<AppDbContext>();

        var n = 0;
        n += await EncryptCrmClientsAsync(db, enc, ct);
        n += await EncryptCrmCommunicationsAsync(db, enc, ct);
        n += await EncryptCrmArtifactMetadataAsync(db, enc, ct);
        n += await EncryptCrmAutomationErrorsAsync(db, enc, ct);
        n += await EncryptChainRunsAsync(db, enc, crmOnly: true, ct);
        if (n > 0)
            log.LogInformation("CRM encryption-at-rest bootstrap rewrote {Count} field(s).", n);
    }

    private static bool NeedsProtect(IDataEncryptor enc, string? value)
        => !string.IsNullOrEmpty(value) && !enc.IsProtected(value);

    private static async Task<int> EncryptCrmClientsAsync(
        AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var n = 0;
        while (true)
        {
            var rows = await db.CrmClients.IgnoreQueryFilters()
                .Where(r => (r.Name != "" && !r.Name.StartsWith(Prefix))
                    || (r.Company != null && r.Company != "" && !r.Company.StartsWith(Prefix))
                    || (r.Email != null && r.Email != "" && !r.Email.StartsWith(Prefix))
                    || (r.Phone != null && r.Phone != "" && !r.Phone.StartsWith(Prefix))
                    || (r.Notes != null && r.Notes != "" && !r.Notes.StartsWith(Prefix)))
                .Take(CrmBatch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                if (NeedsProtect(enc, row.Name)) { row.Name = enc.Protect(row.Name, DataEncryptionPurpose.CrmClient); n++; }
                if (NeedsProtect(enc, row.Company)) { row.Company = enc.Protect(row.Company, DataEncryptionPurpose.CrmClient); n++; }
                if (NeedsProtect(enc, row.Email)) { row.Email = enc.Protect(row.Email, DataEncryptionPurpose.CrmClient); n++; }
                if (NeedsProtect(enc, row.Phone)) { row.Phone = enc.Protect(row.Phone, DataEncryptionPurpose.CrmClient); n++; }
                if (NeedsProtect(enc, row.Notes)) { row.Notes = enc.Protect(row.Notes, DataEncryptionPurpose.CrmClient); n++; }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return n;
    }

    private static async Task<int> EncryptCrmCommunicationsAsync(
        AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = DataEncryptionPurpose.CrmCommunication;
        var n = 0;
        while (true)
        {
            var rows = await db.CrmCommunications.IgnoreQueryFilters()
                .Where(r => (r.BodyProtected != "" && !r.BodyProtected.StartsWith(Prefix))
                    || (r.SubjectProtected != null && r.SubjectProtected != "" && !r.SubjectProtected.StartsWith(Prefix))
                    || (r.RecipientProtected != null && r.RecipientProtected != "" && !r.RecipientProtected.StartsWith(Prefix))
                    || (r.ExternalId != null && r.ExternalId != "" && !r.ExternalId.StartsWith(Prefix))
                    || (r.ErrorProtected != null && r.ErrorProtected != "" && !r.ErrorProtected.StartsWith(Prefix)))
                .Take(CrmBatch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                if (NeedsProtect(enc, row.BodyProtected)) { row.BodyProtected = enc.Protect(row.BodyProtected, p); n++; }
                if (NeedsProtect(enc, row.SubjectProtected)) { row.SubjectProtected = enc.Protect(row.SubjectProtected, p); n++; }
                if (NeedsProtect(enc, row.RecipientProtected)) { row.RecipientProtected = enc.Protect(row.RecipientProtected, p); n++; }
                if (NeedsProtect(enc, row.ExternalId)) { row.ExternalId = enc.Protect(row.ExternalId, p); n++; }
                if (NeedsProtect(enc, row.ErrorProtected)) { row.ErrorProtected = enc.Protect(row.ErrorProtected, p); n++; }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return n;
    }

    private static async Task<int> EncryptCrmArtifactMetadataAsync(
        AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = DataEncryptionPurpose.CrmArtifactMetadata;
        var n = 0;
        while (true)
        {
            var rows = await db.CrmClientArtifacts.IgnoreQueryFilters()
                .Where(r => (r.Title != "" && !r.Title.StartsWith(Prefix))
                    || (r.ObjectKey != "" && !r.ObjectKey.StartsWith(Prefix)))
                .Take(CrmBatch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                if (NeedsProtect(enc, row.Title)) { row.Title = enc.Protect(row.Title, p); n++; }
                if (NeedsProtect(enc, row.ObjectKey)) { row.ObjectKey = enc.Protect(row.ObjectKey, p); n++; }
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return n;
    }

    private static async Task<int> EncryptCrmAutomationErrorsAsync(
        AppDbContext db, IDataEncryptor enc, CancellationToken ct)
    {
        var p = DataEncryptionPurpose.CrmAutomation;
        var n = 0;
        while (true)
        {
            var rows = await db.CrmAutomationQueue
                .Where(r => r.LastError != null && r.LastError != ""
                    && !r.LastError.StartsWith(Prefix))
                .Take(CrmBatch)
                .ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
                if (NeedsProtect(enc, row.LastError))
                {
                    row.LastError = enc.Protect(row.LastError, p);
                    n++;
                }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return n;
    }

    private static async Task<int> EncryptChainRunsAsync(
        AppDbContext db, IDataEncryptor enc, bool crmOnly, CancellationToken ct)
    {
        var p = DataEncryptionPurpose.ChainRun;
        var n = 0;
        while (true)
        {
            IQueryable<ChainRunRow> query = db.ChainRuns.IgnoreQueryFilters();
            if (crmOnly)
                query = query.Where(run => db.CrmChainRuns.IgnoreQueryFilters()
                    .Any(link => link.ChainRunId == run.Id));
            var rows = await query
                .Where(r => (r.StepsJson != "" && !r.StepsJson.StartsWith(Prefix))
                    || (r.FinalOutput != null && r.FinalOutput != ""
                        && !r.FinalOutput.StartsWith(Prefix)))
                .Take(CrmPayloadBatch)
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
        var p = DataEncryptionPurpose.JobSource;
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
        var p = DataEncryptionPurpose.JobRun;
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
        var p = DataEncryptionPurpose.Requirements;
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
        var p = DataEncryptionPurpose.EventPayload;
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
        var p = DataEncryptionPurpose.Decision;
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
        var p = DataEncryptionPurpose.Activity;
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
        var p = DataEncryptionPurpose.Chart;
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
        var p = DataEncryptionPurpose.ToolCall;
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
        var p = DataEncryptionPurpose.PendingRun;
        var rows = await db.PendingRuns.ToListAsync(ct);
        var n = 0;
        foreach (var r in rows)
        {
            if (NeedsProtect(enc, r.Payload)) { r.Payload = enc.Protect(r.Payload, p); n++; }
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
        var purpose = DataEncryptionPurpose.ProjectData;
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
