using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlaceContext.Application.Search;
using PlaceContext.Application.Build;
using PlaceContext.Application.Shared;

namespace PlaceContext.Application.DataSources;

public sealed partial class DataSourcePipeline(IDataSourceStore store, IDataSourceReader reader, IEmbeddingGateway embeddings, IClock clock, IOutcomeReviewer? reviewer = null)
{
    public Task<SourceSyncResult> SyncAsync(Guid projectId, Guid sourceId, CancellationToken ct, bool rebuild = false)
        => RunAsync(projectId, sourceId, null, [], rebuild, ct);

    public Task<SourceSyncResult> IngestAsync(Guid projectId, Guid sourceId, IReadOnlyList<SourceRecord> incoming,
        IReadOnlyList<string> deletedKeys, CancellationToken ct)
        => RunAsync(projectId, sourceId, incoming, deletedKeys, false, ct);

    private async Task<SourceSyncResult> RunAsync(Guid projectId, Guid sourceId, IReadOnlyList<SourceRecord>? incoming,
        IReadOnlyList<string> deletedKeys, bool rebuild, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = await store.GetAsync(projectId, sourceId, ct);
        DataSourceRules.Validate(source);
        var feed = SourceProviders.Get(source.Provider).Kind == "feed";
        if (incoming is not null && (!feed || incoming.Count > 100 || deletedKeys.Count > 100
            || source.Queue is null && incoming.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != incoming.Count
            || deletedKeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 1024)))
        {
            throw new ArgumentException("Only feeds accept pushes. Send up to 100 records or deleted keys with distinct IDs.");
        }
        if (source.Execution is { Status: "running" } active && active.StartedAt > clock.UtcNow.AddMinutes(-10))
        {
            throw new InvalidOperationException("This source is already processing data. Retry after its current run finishes.");
        }
        source = await store.UpdateAsync(source with
        {
            Execution = new(Guid.NewGuid(), "running", "Receiving data", clock.UtcNow)
        }, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        try
        {
            var existing = (await store.ReadDocumentsAsync(projectId, sourceId, token)).ToDictionary(doc => doc.Key, StringComparer.Ordinal);
            if (source.Queue is not null)
            {
                return await PrepareAndQueueAsync(source, existing, incoming, deletedKeys, token);
            }
            var rows = feed ? existing.Values.Select(doc => new SourceRecord(doc.Key, doc.Json)).ToArray()
                : await reader.ReadAsync(source, token);
            if (incoming is not null)
            {
                var merged = rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
                foreach (var row in incoming)
                {
                    merged[row.Key] = ApprovedRecord(source, row);
                }
                foreach (var key in deletedKeys)
                {
                    merged.Remove(key);
                }
                rows = merged.Values.ToArray();
            }
            if (rows.Count > DataSourceRules.MaxRecords || rows.Sum(row => (long)row.Json.Length) > 8_000_000
                || rows.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Key.Length > 1024 || row.Json.Length > DataSourceRules.MaxRecordChars)
                || rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            {
                throw new InvalidOperationException("Snapshot exceeds source limits or contains empty/duplicate keys. Existing data was retained.");
            }
            if (source.EnableEmbeddings && !embeddings.IsEnabled)
            {
                throw new InvalidOperationException("Configure an embedding provider before syncing this source.");
            }
            source = await store.UpdateAsync(source with
            {
                Execution = source.Execution! with { Stage = source.Processing is null ? "Preparing retrieval" : "Jev decision and retrieval", Total = rows.Count }
            }, token);
            var documents = new List<SourceDocument>();
            var changed = 0;
            var policyFingerprint = PolicyFingerprint(source.Processing);
            foreach (var original in rows)
            {
                token.ThrowIfCancellationRequested();
                var row = ApprovedRecord(source, original);
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Json)));
                var reuse = !rebuild && existing.TryGetValue(row.Key, out var previous) && previous.Fingerprint == fingerprint
                    && previous.Review?.PolicyFingerprint == policyFingerprint;
                if (reuse)
                {
                    documents.Add(existing[row.Key]);
                }
                if (!reuse)
                {
                    var review = await ReviewAsync(source, row, policyFingerprint, token);
                    var chunks = review is { Status: "needs-review" } ? [] : await ChunksAsync(source, row.Json, token);
                    documents.Add(new(row.Key, row.Json, fingerprint, chunks, clock.UtcNow, review));
                    changed++;
                }
                if (documents.Count % 10 == 0)
                {
                    source = await store.UpdateAsync(source with
                    {
                        Execution = source.Execution! with { Processed = documents.Count, Held = documents.Count(doc => doc.Review?.Status == "needs-review") }
                    }, token);
                }
            }
            var now = clock.UtcNow;
            var held = documents.Count(doc => doc.Review?.Status == "needs-review");
            var completed = source with
            {
                Execution = source.Execution! with { Status = held > 0 ? "needs-review" : "completed", Stage = held > 0 ? "Review required" : "Retrieval ready",
                    FinishedAt = now, Processed = documents.Count, Held = held }
            };
            await store.CommitAsync(completed, documents, now, token);
            return new(source.Id, documents.Count, changed, existing.Keys.Except(rows.Select(row => row.Key), StringComparer.Ordinal).Count(), now, held);
        }
        catch
        {
            try
            {
                using var statusTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await store.UpdateAsync(source with
                {
                    Execution = source.Execution! with { Status = deadline.IsCancellationRequested ? "interrupted" : "failed",
                        Stage = "Previous data retained", FinishedAt = clock.UtcNow }
                }, statusTimeout.Token);
            }
            catch
            {
                // A newer run or source deletion owns the status; never overwrite it with this attempt.
            }
            throw;
        }
    }

    private static SourceRecord ApprovedRecord(DataSourceDefinition source, SourceRecord row)
    {
        if (string.IsNullOrWhiteSpace(row.Key) || row.Key.Length > 1024 || row.Json.Length > DataSourceRules.MaxRecordChars)
        {
            throw new ArgumentException("Invalid source record or key.");
        }
        using var json = JsonDocument.Parse(row.Json);
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty(source.KeyColumn, out var key) || key.ToString() != row.Key)
        {
            throw new ArgumentException("Each record must contain its matching key field.");
        }
        return new(row.Key, JsonSerializer.Serialize(json.RootElement.EnumerateObject().Where(field => source.Columns.Contains(field.Name))
            .OrderBy(field => field.Name, StringComparer.Ordinal).ToDictionary(field => field.Name, field => field.Value.Clone())));
    }

    public static string? PolicyFingerprint(SourceProcessingPolicy? policy) => policy is null ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { policy.Criteria, policy.MinimumConfidence }))));

    public static bool IsRetrievable(DataSourceDefinition source, SourceDocument document)
        => document.ValidationError is null && (source.Processing is null || document.Review is { Status: "accepted" } review && review.PolicyFingerprint == PolicyFingerprint(source.Processing));

    private async Task<SourceReview?> ReviewAsync(DataSourceDefinition source, SourceRecord row, string? fingerprint, CancellationToken ct)
    {
        if (source.Processing is not { } policy)
        {
            return null;
        }
        try
        {
            var response = await (reviewer ?? throw new InvalidOperationException()).EvaluateAsync(
                PromptText.Render("DataSources.Prompts.InboundReview", ("criteria", policy.Criteria)), row.Json, ct, source.ProjectId);
            if (response["noul"] is not System.Text.Json.Nodes.JsonValue value || !value.TryGetValue<double>(out var score)
                || !double.IsFinite(score) || score is < 0 or > 1 || response["model"]?.GetValue<string>() is not { Length: > 0 and <= 200 } model)
            {
                throw new InvalidOperationException();
            }
            return new(score >= policy.MinimumConfidence ? "accepted" : "needs-review", score, model, fingerprint!, clock.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Jev could not evaluate incoming data. Existing data was retained. Check the configured decision service and retry.");
        }
    }

    private async Task<IReadOnlyList<SourceChunk>> ChunksAsync(DataSourceDefinition source, string json, CancellationToken ct)
    {
        var text = string.Join("\n", JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.Select(field => $"{field.Key}: {field.Value}"));
        var parts = new List<string>();
        for (var start = 0; start < text.Length;)
        {
            var end = Math.Min(start + DataSourceRules.ChunkChars, text.Length);
            if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
            parts.Add(text[start..end]);
            start = end;
        }
        IReadOnlyList<float[]> vectors = [];
        if (source.EnableEmbeddings && parts.Count > 0)
        {
            vectors = await EmbedAsync(parts, ct);
            if (vectors.Count != parts.Count || vectors.Any(vector => vector.Length != embeddings.Dimensions
                || vector.Length == 0 || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0)))
            {
                throw new InvalidOperationException("Embedding provider returned incomplete vectors. Existing data was retained.");
            }
        }
        return parts.Select((part, index) => new SourceChunk(part, source.EnableEmbeddings ? vectors[index] : null)).ToArray();
    }

    public async Task<DataSourceSearchResult> SearchAsync(Guid projectId, Guid sourceId, string query, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1000 || take is < 1 or > 25)
        {
            throw new ArgumentException("Use a query of 1–1000 characters and a result limit of 1–25.");
        }
        var source = await store.GetAsync(projectId, sourceId, ct);
        var documents = (await store.ReadDocumentsAsync(projectId, sourceId, ct)).Where(doc => IsRetrievable(source, doc)).ToArray();
        float[]? vector = null;
        if (source.EnableEmbeddings)
        {
            if (!embeddings.IsEnabled)
            {
                throw new InvalidOperationException("The configured embedding provider is unavailable.");
            }
            var vectors = await EmbedAsync([query], ct);
            vector = vectors.SingleOrDefault();
            if (vector is null || vector.Length != embeddings.Dimensions || vector.Length == 0
                || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0))
            {
                throw new InvalidOperationException("The embedding provider returned an invalid query vector.");
            }
            if (documents.SelectMany(doc => doc.Chunks).Any(chunk => chunk.Vector?.Length != vector.Length))
            {
                throw new InvalidOperationException("Rebuild this source index after changing the embedding model.");
            }
        }
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hits = documents.SelectMany(doc => doc.Chunks.Select((chunk, index) =>
        {
            var score = vector is null ? terms.Count(term => chunk.Text.Contains(term, StringComparison.OrdinalIgnoreCase) || source.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))) / (double)terms.Length
                : Cosine(vector, chunk.Vector!);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(doc.Key)))[..16];
            return new DataSourceHit($"{sourceId:N}:{key}:{doc.Fingerprint[..12]}:{index}", sourceId, source.Name,
                $"{source.Schema}.{source.Table}", doc.Key, index, chunk.Text, source.Tags, score, doc.UpdatedAt);
        })).Where(hit => hit.Score > 0).OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Citation, StringComparer.Ordinal).Take(take).ToArray();
        return new(vector is null ? "keyword" : "semantic", hits);
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        try
        {
            return await embeddings.EmbedAsync(texts, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Embedding request failed. Existing source data was retained.");
        }
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += (double)left[i] * right[i];
            a += (double)left[i] * left[i];
            b += (double)right[i] * right[i];
        }
        return a == 0 || b == 0 ? 0 : dot / Math.Sqrt(a * b);
    }
}
