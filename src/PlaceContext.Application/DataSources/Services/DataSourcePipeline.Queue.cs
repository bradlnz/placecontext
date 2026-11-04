namespace PlaceContext.Application.DataSources;

public sealed partial class DataSourcePipeline
{
    private async Task<SourceSyncResult> PrepareAndQueueAsync(DataSourceDefinition source,
        Dictionary<string, SourceDocument> existing, IReadOnlyList<SourceRecord>? incoming, IReadOnlyList<string> deletedKeys, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var prepared = SourceProviders.Get(source.Provider).Kind == "feed"
            ? PrepareFeed(source, existing, incoming, deletedKeys, now)
            : await PrepareSnapshotAsync(source, now, ct);
        prepared = prepared.Select(document => existing.TryGetValue(document.Key, out var old) && old.Fingerprint == document.Fingerprint
            ? old : document).ToArray();
        if (prepared.Count > DataSourceRules.RecordLimit(source) || prepared.Sum(document => (long)document.Json.Length) > DataSourceRules.SnapshotCharLimit(source))
        {
            throw new InvalidOperationException("Prepared data exceeds source limits. Previous data was retained.");
        }
        var changed = prepared.Where(document => !existing.TryGetValue(document.Key, out var old) || old.Fingerprint != document.Fingerprint).ToArray();
        var deleted = existing.Keys.Except(prepared.Select(document => document.Key), StringComparer.Ordinal).ToArray();
        var heldCount = prepared.Count(document => document.ValidationError is not null);
        var queuedCount = changed.Count(document => document.ValidationError is null);
        source = source with
        {
            Execution = source.Execution! with
            {
                Status = heldCount > 0 ? "needs-review" : "completed", Stage = "Prepared data delivered to queue",
                FinishedAt = now, Total = prepared.Count, Processed = prepared.Count, Held = heldCount
            }
        };
        // The store commits prepared records and queue submissions in the same database transaction.
        await store.CommitAsync(source, prepared, now, ct);
        return new(source.Id, prepared.Count, changed.Length, deleted.Length, now, heldCount, queuedCount);
    }

    private async Task<IReadOnlyList<SourceDocument>> PrepareSnapshotAsync(DataSourceDefinition source, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await reader.ReadAsync(source, ct);
        // Readers fetch one extra row to detect truncation; deduplication must not hide an incomplete snapshot.
        if (rows.Count > DataSourceRules.RecordLimit(source))
        {
            throw new InvalidOperationException("Source exceeds the snapshot limit. Previous data was retained.");
        }
        return SourcePreparation.Prepare(source, rows, now);
    }

    private static IReadOnlyList<SourceDocument> PrepareFeed(DataSourceDefinition source,
        Dictionary<string, SourceDocument> existing, IReadOnlyList<SourceRecord>? incoming, IReadOnlyList<string> deletedKeys, DateTimeOffset now)
    {
        // Retain held errors until a corrected event arrives; never turn a held marker into a valid event.
        var merged = SourcePreparation.Prepare(source, existing.Values.Where(document => document.ValidationError is null)
            .Select(document => new SourceRecord(document.Key, document.Json)).ToArray(), now).ToDictionary(document => document.Key);
        foreach (var held in existing.Values.Where(document => document.ValidationError is not null))
        {
            merged[held.Key] = held;
        }
        foreach (var document in SourcePreparation.Prepare(source, incoming ?? [], now))
        {
            merged[document.Key] = document;
        }
        foreach (var key in deletedKeys)
        {
            merged.Remove(key);
        }
        return merged.Values.ToArray();
    }

}
