using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Embeddings;

/// <summary>
/// pgvector-backed universal content indexer. Source text is encrypted with
/// <see cref="DataEncryptionPurpose.EmbeddingText"/>; vectors stay as float arrays for ANN search
/// (encrypting vectors would break cosine operators). Self-inits <c>content_embeddings</c> lazily.
/// </summary>
public sealed class ContentIndexer : IContentIndexer
{
    public const int MaxTextChars = 8000;
    private static int _state; // 0 unknown, 1 ready, 2 unavailable
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IEmbeddingGateway _gateway;
    private readonly IDataEncryptor _enc;
    private readonly ILogger<ContentIndexer> _log;
    private static string TextPurpose => DataEncryptionPurpose.EmbeddingText;

    public ContentIndexer(
        AppDbContext db,
        ICurrentTenant tenant,
        IEmbeddingGateway gateway,
        IDataEncryptor enc,
        ILogger<ContentIndexer> log)
    {
        _db = db;
        _tenant = tenant;
        _gateway = gateway;
        _enc = enc;
        _log = log;
    }

    public bool IsEnabled => _gateway.IsEnabled && _gateway.Dimensions > 0;

    public async Task IndexAsync(Guid projectId, string kind, string sourceKey, string text, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sourceKey)) return;
        await IndexManyAsync(projectId, kind, new[] { (sourceKey, text) }, ct);
    }

    public async Task IndexManyAsync(
        Guid projectId, string kind, IReadOnlyList<(string SourceKey, string Text)> items, CancellationToken ct = default)
    {
        if (!IsEnabled || items.Count == 0) return;
        if (!await EnsureReadyAsync(ct)) return;

        var prepared = items
            .Where(i => !string.IsNullOrWhiteSpace(i.SourceKey) && !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => (
                Key: i.SourceKey.Trim(),
                Text: i.Text.Length > MaxTextChars ? i.Text[..MaxTextChars] : i.Text.Trim()))
            .Where(i => i.Text.Length > 0)
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        if (prepared.Count == 0) return;

        try
        {
            // Batch embed (gateway may have its own batch limit — chunk at 32).
            const int batch = 32;
            for (var offset = 0; offset < prepared.Count; offset += batch)
            {
                var slice = prepared.Skip(offset).Take(batch).ToList();
                var vectors = await _gateway.EmbedAsync(slice.Select(s => s.Text).ToList(), ct);
                if (vectors.Count != slice.Count) continue;

                var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
                await OpenAsync(conn, ct);
                for (var i = 0; i < slice.Count; i++)
                {
                    if (vectors[i].Length == 0) continue;
                    var cipher = _enc.Protect(slice[i].Text, TextPurpose);
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        INSERT INTO content_embeddings
                          (id, tenant_id, project_id, kind, source_key, text_cipher, embedding, created_at)
                        VALUES (@id, @tenant, @project, @kind, @key, @text, @vec::vector, @created)
                        ON CONFLICT (tenant_id, project_id, kind, source_key) DO UPDATE SET
                          text_cipher = EXCLUDED.text_cipher,
                          embedding = EXCLUDED.embedding,
                          created_at = EXCLUDED.created_at
                        """;
                    cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                    cmd.Parameters.AddWithValue("tenant", _tenant.TenantId);
                    cmd.Parameters.AddWithValue("project", projectId);
                    cmd.Parameters.AddWithValue("kind", kind);
                    cmd.Parameters.AddWithValue("key", slice[i].Key);
                    cmd.Parameters.AddWithValue("text", cipher);
                    cmd.Parameters.AddWithValue("vec", ToVectorLiteral(vectors[i]));
                    cmd.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Content index failed for project {ProjectId} kind {Kind} ({Count} items).",
                projectId, kind, items.Count);
        }
    }

    public async Task<IReadOnlyList<ContentSearchHit>> SearchAsync(
        Guid projectId, string query, int take = 10, string? kind = null, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query) || !await EnsureReadyAsync(ct))
            return Array.Empty<ContentSearchHit>();

        take = Math.Clamp(take, 1, 50);
        try
        {
            var vectors = await _gateway.EmbedAsync(new[] { query.Length > MaxTextChars ? query[..MaxTextChars] : query }, ct);
            if (vectors.Count == 0 || vectors[0].Length == 0) return Array.Empty<ContentSearchHit>();

            var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
            await OpenAsync(conn, ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = kind is null
                ? """
                  SELECT kind, source_key, text_cipher, created_at,
                         1 - (embedding <=> @q::vector) AS score
                  FROM content_embeddings
                  WHERE tenant_id = @tenant AND project_id = @project
                  ORDER BY embedding <=> @q::vector
                  LIMIT @take
                  """
                : """
                  SELECT kind, source_key, text_cipher, created_at,
                         1 - (embedding <=> @q::vector) AS score
                  FROM content_embeddings
                  WHERE tenant_id = @tenant AND project_id = @project AND kind = @kind
                  ORDER BY embedding <=> @q::vector
                  LIMIT @take
                  """;
            cmd.Parameters.AddWithValue("q", ToVectorLiteral(vectors[0]));
            cmd.Parameters.AddWithValue("tenant", _tenant.TenantId);
            cmd.Parameters.AddWithValue("project", projectId);
            cmd.Parameters.AddWithValue("take", take);
            if (kind is not null) cmd.Parameters.AddWithValue("kind", kind);

            var hits = new List<ContentSearchHit>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var plain = _enc.Unprotect(reader.GetString(2), TextPurpose);
                hits.Add(new ContentSearchHit(
                    reader.GetString(0),
                    reader.GetString(1),
                    plain,
                    reader.GetDouble(4),
                    reader.GetFieldValue<DateTimeOffset>(3)));
            }
            return hits;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Content search failed for project {ProjectId}.", projectId);
            return Array.Empty<ContentSearchHit>();
        }
    }

    private async Task<bool> EnsureReadyAsync(CancellationToken ct)
    {
        if (_state == 1) return true;
        if (_state == 2) return false;
        if (!IsEnabled) { _state = 2; return false; }

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
                CREATE TABLE IF NOT EXISTS content_embeddings (
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    project_id uuid NOT NULL,
                    kind text NOT NULL,
                    source_key text NOT NULL,
                    text_cipher text NOT NULL,
                    embedding vector({_gateway.Dimensions}) NOT NULL,
                    created_at timestamptz NOT NULL,
                    UNIQUE (tenant_id, project_id, kind, source_key)
                );
                CREATE INDEX IF NOT EXISTS ix_content_embeddings_tenant_project
                    ON content_embeddings (tenant_id, project_id);
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            _state = 1;
            return true;
        }
        catch (Exception ex)
        {
            _state = 2;
            _log.LogWarning(ex, "Content embedding store unavailable — RAG disabled.");
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
}
