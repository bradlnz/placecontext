using System.Globalization;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// pgvector-backed <see cref="IRunEmbeddingRepository"/>. To avoid breaking deployments without the
/// extension (and to keep this off the migration path), it <b>lazily self-initializes</b> the
/// <c>vector</c> extension + <c>job_run_embeddings</c> table on first use, and <b>degrades gracefully</b>
/// (logs once, then no-ops) if embeddings are disabled or pgvector is unavailable. Cosine similarity via
/// the <c>&lt;=&gt;</c> operator. Tenant-scoped by an explicit <c>tenant_id</c> column.
/// </summary>
public sealed class EfRunEmbeddingRepository : IRunEmbeddingRepository
{
    private static int _state; // 0 = unknown, 1 = ready, 2 = unavailable
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IEmbeddingGateway _embeddings;
    private readonly ILogger<EfRunEmbeddingRepository> _log;

    public EfRunEmbeddingRepository(
        AppDbContext db, ICurrentTenant tenant, IEmbeddingGateway embeddings, ILogger<EfRunEmbeddingRepository> log)
    {
        _db = db;
        _tenant = tenant;
        _embeddings = embeddings;
        _log = log;
    }

    public async Task AddAsync(RunEmbedding embedding, CancellationToken ct = default)
    {
        if (!await EnsureReadyAsync(ct)) return;

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await OpenAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO job_run_embeddings
              (id, tenant_id, job_run_id, job_id, project_id, text, embedding, created_at)
            VALUES (@id, @tenant, @run, @job, @project, @text, @vec::vector, @created)
            """;
        cmd.Parameters.AddWithValue("id", embedding.Id);
        cmd.Parameters.AddWithValue("tenant", _tenant.TenantId);
        cmd.Parameters.AddWithValue("run", embedding.JobRunId);
        cmd.Parameters.AddWithValue("job", embedding.JobId);
        cmd.Parameters.AddWithValue("project", embedding.ProjectId);
        cmd.Parameters.AddWithValue("text", embedding.Text);
        cmd.Parameters.AddWithValue("vec", ToVectorLiteral(embedding.Vector));
        cmd.Parameters.AddWithValue("created", embedding.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RunEmbeddingMatch>> SearchAsync(
        Guid projectId, float[] queryVector, int take, CancellationToken ct = default)
    {
        if (queryVector.Length == 0 || !await EnsureReadyAsync(ct))
            return Array.Empty<RunEmbeddingMatch>();

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await OpenAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, job_run_id, job_id, project_id, text, created_at,
                   1 - (embedding <=> @q::vector) AS score
            FROM job_run_embeddings
            WHERE tenant_id = @tenant AND project_id = @project
            ORDER BY embedding <=> @q::vector
            LIMIT @take
            """;
        cmd.Parameters.AddWithValue("q", ToVectorLiteral(queryVector));
        cmd.Parameters.AddWithValue("tenant", _tenant.TenantId);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("take", take);

        var results = new List<RunEmbeddingMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var embedding = RunEmbedding.Rehydrate(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetString(4), Array.Empty<float>(), reader.GetFieldValue<DateTimeOffset>(5));
            results.Add(new RunEmbeddingMatch(embedding, reader.GetDouble(6)));
        }
        return results;
    }

    public async Task<IReadOnlyList<RunEmbedding>> ListForProjectAsync(
        Guid projectId, int take = 200, CancellationToken ct = default)
    {
        if (!await EnsureReadyAsync(ct)) return Array.Empty<RunEmbedding>();

        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await OpenAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, job_run_id, job_id, project_id, text, embedding::text, created_at
            FROM job_run_embeddings
            WHERE tenant_id = @tenant AND project_id = @project
            ORDER BY created_at DESC
            LIMIT @take
            """;
        cmd.Parameters.AddWithValue("tenant", _tenant.TenantId);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("take", take);

        var results = new List<RunEmbedding>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(RunEmbedding.Rehydrate(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetString(4), ParseVectorLiteral(reader.GetString(5)),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return results;
    }

    // ── Lazy init / graceful degradation ───────────────────────────────────────────────────────────

    private async Task<bool> EnsureReadyAsync(CancellationToken ct)
    {
        if (_state == 1) return true;
        if (_state == 2) return false;
        if (!_embeddings.IsEnabled || _embeddings.Dimensions <= 0) { _state = 2; return false; }

        await Gate.WaitAsync(ct);
        try
        {
            if (_state == 1) return true;
            if (_state == 2) return false;

            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            await OpenAsync(conn, ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE TABLE IF NOT EXISTS job_run_embeddings (
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    job_run_id uuid NOT NULL,
                    job_id uuid NOT NULL,
                    project_id uuid NOT NULL,
                    text text NOT NULL,
                    embedding vector({_embeddings.Dimensions}) NOT NULL,
                    created_at timestamptz NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_job_run_embeddings_tenant_project
                    ON job_run_embeddings (tenant_id, project_id);
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            _state = 1;
            return true;
        }
        catch (Exception ex)
        {
            _state = 2;
            _log.LogWarning(ex, "Run-embedding vector store unavailable (pgvector not installed?) — semantic search disabled.");
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task OpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
    }

    private static string ToVectorLiteral(float[] vector)
        => "[" + string.Join(",", vector.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

    /// <summary>Parses pgvector's text form (<c>[0.1,0.2,…]</c>) back into a float array.</summary>
    private static float[] ParseVectorLiteral(string literal)
    {
        var inner = literal.Trim().Trim('[', ']');
        if (inner.Length == 0) return Array.Empty<float>();
        return inner.Split(',')
            .Select(p => float.Parse(p, CultureInfo.InvariantCulture))
            .ToArray();
    }
}
