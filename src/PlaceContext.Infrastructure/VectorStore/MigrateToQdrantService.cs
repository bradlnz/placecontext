using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.VectorStore;

/// <summary>
/// One-shot migration from pgvector (<c>job_run_embeddings</c> and <c>content_embeddings</c> tables)
/// into Qdrant collections. Runs once on the first startup after deployment — subsequent starts
/// skip it. Idempotent: Qdrant upserts replace existing points by the same ID.
/// </summary>
public sealed class MigrateToQdrantService : IHostedService
{
    private static bool _migrated;

    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<MigrateToQdrantService> _log;

    public MigrateToQdrantService(IServiceProvider sp, IConfiguration config, ILogger<MigrateToQdrantService> log)
    {
        _sp = sp;
        _config = config;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_migrated) return;
        _migrated = true;

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enc = scope.ServiceProvider.GetRequiredService<IDataEncryptor>();
        var qdrantUrl = _config["PlaceContext:Qdrant:Endpoint"];
        if (string.IsNullOrWhiteSpace(qdrantUrl))
        {
            _log.LogInformation("Qdrant endpoint not configured — skipping migration.");
            return;
        }

        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
        var baseUrl = qdrantUrl.TrimEnd('/');
        var purpose = IDataEncryptor.Purpose.EmbeddingText;

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);

        try
        {
            await MigrateRunEmbeddingsAsync(conn, enc, http, baseUrl, purpose, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Run-embedding table does not exist or migration failed — skipping.");
        }

        try
        {
            await MigrateContentEmbeddingsAsync(conn, enc, http, baseUrl, purpose, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Content-embedding table does not exist or migration failed — skipping.");
        }

        _log.LogInformation("Pgvector → Qdrant migration complete.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task MigrateRunEmbeddingsAsync(
        System.Data.Common.DbConnection conn, IDataEncryptor enc, HttpClient http,
        string baseUrl, string purpose, CancellationToken ct)
    {
        var npgsql = (NpgsqlConnection)conn;
        await using var cmd = npgsql.CreateCommand();
        cmd.CommandText = """
            SELECT id, tenant_id, job_run_id, job_id, project_id, text, embedding::text, created_at
            FROM job_run_embeddings
            """;

        var rows = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var tenantId = reader.GetGuid(1);
            var jobRunId = reader.GetGuid(2);
            var jobId = reader.GetGuid(3);
            var projectId = reader.GetGuid(4);
            var cipher = reader.GetString(5);
            var vectorText = reader.GetString(6);
            var createdAt = reader.GetFieldValue<DateTimeOffset>(7);

            var plain = enc.Unprotect(cipher, purpose);
            var vector = ParseVectorLiteral(vectorText);

            var point = new
            {
                id = id.ToString("N"),
                vector,
                payload = new
                {
                    tenant_id = tenantId.ToString("N"),
                    job_run_id = jobRunId.ToString("N"),
                    job_id = jobId.ToString("N"),
                    project_id = projectId.ToString("N"),
                    text = plain,
                    created_at = createdAt.ToString("O"),
                }
            };

            await http.PutAsync($"{baseUrl}/collections/run_embeddings/points",
                JsonContent.Create(new { points = new[] { point } }), ct);
            rows++;
        }

        _log.LogInformation("Migrated {Count} run embeddings to Qdrant.", rows);
    }

    private async Task MigrateContentEmbeddingsAsync(
        System.Data.Common.DbConnection conn, IDataEncryptor enc, HttpClient http,
        string baseUrl, string purpose, CancellationToken ct)
    {
        var npgsql = (NpgsqlConnection)conn;
        await using var cmd = npgsql.CreateCommand();
        cmd.CommandText = """
            SELECT id, tenant_id, project_id, kind, source_key, text_cipher, embedding::text, created_at
            FROM content_embeddings
            """;

        var rows = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var tenantId = reader.GetGuid(1);
            var projectId = reader.GetGuid(2);
            var kind = reader.GetString(3);
            var sourceKey = reader.GetString(4);
            var cipher = reader.GetString(5);
            var vectorText = reader.GetString(6);
            var createdAt = reader.GetFieldValue<DateTimeOffset>(7);

            var plain = enc.Unprotect(cipher, purpose);
            var vector = ParseVectorLiteral(vectorText);

            var point = new
            {
                id = id.ToString("N"),
                vector,
                payload = new
                {
                    tenant_id = tenantId.ToString("N"),
                    project_id = projectId.ToString("N"),
                    kind,
                    source_key = sourceKey,
                    text = plain,
                    created_at = createdAt.ToString("O"),
                }
            };

            await http.PutAsync($"{baseUrl}/collections/content_embeddings/points",
                JsonContent.Create(new { points = new[] { point } }), ct);
            rows++;
        }

        _log.LogInformation("Migrated {Count} content embeddings to Qdrant.", rows);
    }

    private static float[] ParseVectorLiteral(string literal)
    {
        var inner = literal.Trim().Trim('[', ']');
        if (inner.Length == 0) return [];
        return inner.Split(',').Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
    }
}
