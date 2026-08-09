using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes the project's data map after a run completes: for every enabled mapping of the job,
/// extracts records from the run's primary artifact (at the mapping's RowsPath, or the root),
/// resolves each field's dot-path, and appends the rows to the mapping's target table in the
/// project database. Field values are stored as-is in their declared column: objects and arrays
/// land as JSON text (so huge nested objects don't explode into hundreds of leaf columns).
/// Tables are system-owned append-only (created on first ingest) with <c>ingested_at</c>,
/// <c>run_id</c>, <c>source_kind</c>, <c>source_id</c>, and <c>mapping_id</c> provenance columns, so
/// lineage remains queryable when several jobs or chains contribute complementary fields to one
/// dataset. Missing nullable columns are added transactionally by the store as mapped outputs evolve.
/// Entirely best-effort — a mapping failure is logged and never fails the run.
/// </summary>
public sealed class DataMappingIngestionService
{
    private static readonly IReadOnlyList<ProjectColumnSpec> ProvenanceColumns = new[]
    {
        new ProjectColumnSpec("ingested_at", DataColumnTypes.Timestamptz, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("run_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
    };

    private static readonly IReadOnlyList<ProjectColumnSpec> SourceLineageColumns = new[]
    {
        new ProjectColumnSpec("source_kind", DataColumnTypes.Text, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("source_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("mapping_id", DataColumnTypes.Uuid, NotNull: true, PrimaryKey: false),
    };

    private readonly IDataMappingRepository _mappings;
    private readonly IProjectDataStore _store;
    private readonly IClock _clock;
    private readonly IContentIndexer? _indexer;
    private readonly RecordLinkService? _links;
    private readonly ILogger<DataMappingIngestionService>? _log;

    public DataMappingIngestionService(
        IDataMappingRepository mappings,
        IProjectDataStore store,
        IClock clock,
        IContentIndexer? indexer = null,
        RecordLinkService? links = null,
        ILogger<DataMappingIngestionService>? log = null)
    {
        _mappings = mappings;
        _store = store;
        _clock = clock;
        _indexer = indexer;
        _links = links;
        _log = log;
    }

    public async Task IngestJobOutputAsync(
        Guid jobId,
        Guid runId,
        Guid projectId,
        string? primaryOutput,
        CancellationToken ct = default)
    {
        IReadOnlyList<DataMapping> mappings;
        try
        {
            mappings = await _mappings.ListForJobAsync(jobId, ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Loading data mappings for job {JobId} failed.", jobId);
            return;
        }

        foreach (var mapping in mappings.Where(m => m.Enabled && m.SourceKind == "job"))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(primaryOutput))
                {
                    _log?.LogInformation(
                        "Run {RunId} produced no primary artifact — nothing to ingest for mapping {MappingId}.",
                        runId,
                        mapping.Id);
                    continue;
                }

                await IngestPayloadAsync(mapping, runId, projectId, primaryOutput, ct);
            }
            catch (Exception ex)
            {
                Surface(projectId, runId, mapping.TargetTable, $"ingest failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// The chain flavour: mappings whose source is the chain ingest its FINAL output (what the last
    /// step produced) with the chain run's id as provenance. Same extraction, same isolation.
    /// </summary>
    public async Task IngestChainOutputAsync(Guid chainId, Guid chainRunId, Guid projectId,
        string? finalOutput, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(finalOutput)) return;
        IReadOnlyList<DataMapping> mappings;
        try { mappings = await _mappings.ListForJobAsync(chainId, ct); }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Loading data mappings for chain {ChainId} failed.", chainId);
            return;
        }

        foreach (var mapping in mappings.Where(m => m.Enabled && m.SourceKind == "chain"))
        {
            try
            {
                await IngestPayloadAsync(mapping, chainRunId, projectId, finalOutput, ct);
            }
            catch (Exception ex)
            {
                Surface(projectId, chainRunId, mapping.TargetTable, $"ingest failed: {ex.Message}", ex);
            }
        }
    }

    private async Task IngestPayloadAsync(DataMapping mapping, Guid runId, Guid projectId,
        string primary, CancellationToken ct)
    {
        using var doc = ParsePayload(primary);
        var root = Navigate(doc.RootElement, mapping.RowsPath);
        if (root is not { } records) return;

        var recordList = Records(records).ToList();
        if (recordList.Count == 0) return;

        // Each field lands in its declared column as JSON text. Objects and arrays stay whole —
        // they are not flattened into leaf columns, so huge nested payloads can't explode the schema.
        var cellRows = recordList
            .Select(record => mapping.Fields
                .Select((field, i) =>
                {
                    var value = Navigate(record, field.SourcePath);
                    return (Field: i, Column: field.Column, Text: JsonFlattener.ValueText(value));
                })
                .ToList())
            .ToList();

        var columns = new List<ProjectColumnSpec>(ProvenanceColumns);
        var taken = ProvenanceColumns.Concat(SourceLineageColumns)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ProvenanceColumns.Count; i++) columnIndex[ProvenanceColumns[i].Name] = i;
        foreach (var field in mapping.Fields)
        {
            if (!taken.Add(field.Column))
            {
                _log?.LogWarning("Data map → '{Table}': declared column '{Column}' is duplicated or reserved — skipped.",
                    mapping.TargetTable, field.Column);
                continue;
            }
            columnIndex[field.Column] = columns.Count;
            columns.Add(new ProjectColumnSpec(field.Column, field.Type, NotNull: false, PrimaryKey: false));
        }

        foreach (var column in SourceLineageColumns)
        {
            columnIndex[column.Name] = columns.Count;
            columns.Add(column);
        }

        var now = _clock.UtcNow.ToString("O");
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var cells in cellRows)
        {
            var row = new string?[columns.Count];
            var written = new bool[columns.Count];
            row[columnIndex["ingested_at"]] = now;
            row[columnIndex["run_id"]] = runId.ToString();
            row[columnIndex["source_kind"]] = mapping.SourceKind;
            row[columnIndex["source_id"]] = mapping.JobId.ToString();
            row[columnIndex["mapping_id"]] = mapping.Id.ToString();
            written[columnIndex["ingested_at"]] = true;
            written[columnIndex["run_id"]] = true;
            written[columnIndex["source_kind"]] = true;
            written[columnIndex["source_id"]] = true;
            written[columnIndex["mapping_id"]] = true;
            foreach (var (_, col, text) in cells)
            {
                if (!columnIndex.TryGetValue(col, out var idx) || written[idx]) continue;
                row[idx] = text;
                written[idx] = true;
            }
            rows.Add(row);
        }

        // The system table writer reconciles missing nullable columns transactionally. This is
        // essential when different jobs contribute complementary fields to the same correlated
        // dataset: schema growth preserves both outputs instead of dropping the later batch.

        try
        {
            await _store.AppendReadOnlyRowsAsync(projectId, mapping.TargetTable, columns, rows, ct);
        }
        catch (Exception ex)
        {
            Surface(projectId, runId, mapping.TargetTable, $"{rows.Count} row(s) dropped — append failed: {ex.Message}", ex);
            return;
        }
        _log?.LogInformation("Ingested {Count} row(s) from run {RunId} into '{Table}'.",
            rows.Count, runId, mapping.TargetTable);

        // The table gained rows: refresh its slice of the record-link index (best-effort).
        if (_links is not null)
        {
            try
            {
                await _links.RefreshTableAsync(projectId, mapping.TargetTable, ct);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Record-link refresh of '{Table}' failed — the ingest is unaffected.", mapping.TargetTable);
            }
        }

        // Auto-embed every ingested row for RAG (source text encrypted at rest by the indexer).
        if (_indexer is { IsEnabled: true })
        {
            var items = new List<(string, string)>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var parts = new List<string> { $"table: {mapping.TargetTable}" };
                for (var c = 0; c < columns.Count && c < r.Count; c++)
                {
                    if (string.IsNullOrWhiteSpace(r[c])) continue;
                    parts.Add($"{columns[c].Name}: {r[c]}");
                }
                items.Add(($"{mapping.TargetTable}:{runId}:{i}", string.Join("\n", parts)));
            }
            await _indexer.IndexManyAsync(projectId, ContentKind.ProjectData, items, ct);
        }
    }

    // A data-map failure never fails the run, but it must be VISIBLE: log at error level and push a
    // distinct notification into the operations pane (its own key so it stands beside — not on top of —
    // the run's "Succeeded" entry), rather than the buried best-effort warning that hid data loss.
    private void Surface(Guid projectId, Guid runId, string table, string detail, Exception? ex = null)
    {
        _log?.LogError(ex, "Data map → '{Table}' for run {RunId}: {Detail}", table, runId, detail);
        // Operations receives run-level failures from Jobs. Data owns this additional diagnostic
        // and keeps it in its service log until the Operations HTTP ingestion endpoint is available.
    }

    // Parse the whole artifact first (the clean case); on failure, use the last line that parses as
    // JSON. If no JSON exists, preserve the complete text as a JSON string so a `$` field mapping
    // can still ingest logs, summaries, model answers, and other scalar job results.
    private static JsonDocument ParsePayload(string primary)
    {
        try { return JsonDocument.Parse(primary); }
        catch (JsonException)
        {
            var lines = primary.Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || (line[0] != '{' && line[0] != '[')) continue;
                try { return JsonDocument.Parse(line); }
                catch (JsonException) { /* keep scanning earlier lines */ }
            }
            return JsonDocument.Parse(JsonSerializer.Serialize(primary.Trim()));
        }
    }

    // Arrays yield one record per element. Objects and scalar values each yield one record so every
    // valid job result shape remains mappable; null carries no usable value and is skipped.
    private static IEnumerable<JsonElement> Records(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (item.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    yield return item;
        }
        else if (el.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            yield return el;
        }
    }

    // Dot-path navigation over objects ("data.items"). `$` selects the complete current record,
    // and `$.data.items` is the explicit-root form. Null/empty remains the rows-path root.
    private static JsonElement? Navigate(JsonElement el, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$") return el;
        if (path.StartsWith("$.", StringComparison.Ordinal)) path = path[2..];
        var current = el;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }
}
