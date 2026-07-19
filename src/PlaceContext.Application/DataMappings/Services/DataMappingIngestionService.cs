using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes the project's data map after a run completes: for every enabled mapping of the job,
/// extracts records from the run's primary artifact (at the mapping's RowsPath, or the root),
/// resolves each field's dot-path, and appends the rows to the mapping's target table in the
/// project database. Field values are stored as-is in their declared column: objects and arrays
/// land as JSON text (so huge nested objects don't explode into hundreds of leaf columns).
/// Tables are system-owned append-only (created on first ingest) with <c>ingested_at</c>/<c>run_id</c>
/// provenance columns, so lineage back to the run is always queryable. Entirely best-effort — a
/// mapping failure is logged and never fails the run.
/// </summary>
public sealed class DataMappingIngestionService
{
    private static readonly IReadOnlyList<ProjectColumnSpec> ProvenanceColumns = new[]
    {
        new ProjectColumnSpec("ingested_at", "timestamptz", NotNull: true, PrimaryKey: false),
        new ProjectColumnSpec("run_id", "uuid", NotNull: true, PrimaryKey: false),
    };

    private readonly IDataMappingRepository _mappings;
    private readonly IProjectDataStore _store;
    private readonly IClock _clock;
    private readonly IContentIndexer? _indexer;
    private readonly IRunStatusNotifier? _notifier;
    private readonly RecordLinkService? _links;
    private readonly ILogger<DataMappingIngestionService>? _log;

    public DataMappingIngestionService(
        IDataMappingRepository mappings,
        IProjectDataStore store,
        IClock clock,
        IContentIndexer? indexer = null,
        IRunStatusNotifier? notifier = null,
        RecordLinkService? links = null,
        ILogger<DataMappingIngestionService>? log = null)
    {
        _mappings = mappings;
        _store = store;
        _clock = clock;
        _indexer = indexer;
        _notifier = notifier;
        _links = links;
        _log = log;
    }

    public async Task IngestAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        IReadOnlyList<DataMapping> mappings;
        try
        {
            mappings = await _mappings.ListForJobAsync(job.Id, ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Loading data mappings for job {JobId} failed.", job.Id);
            return;
        }

        foreach (var mapping in mappings.Where(m => m.Enabled && m.SourceKind == "job"))
        {
            try
            {
                await IngestOneAsync(mapping, run, ct);
            }
            catch (Exception ex)
            {
                Surface(run.ProjectId, run.Id, mapping.TargetTable, $"ingest failed: {ex.Message}", ex);
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

    private async Task IngestOneAsync(DataMapping mapping, JobRun run, CancellationToken ct)
    {
        var primary = PrimaryArtifact(run);
        if (string.IsNullOrWhiteSpace(primary))
        {
            _log?.LogInformation("Run {RunId} produced no primary artifact — nothing to ingest for mapping {MappingId}.",
                run.Id, mapping.Id);
            return;
        }
        await IngestPayloadAsync(mapping, run.Id, run.ProjectId, primary, ct);
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
        var taken = new HashSet<string>(ProvenanceColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ProvenanceColumns.Count; i++) columnIndex[ProvenanceColumns[i].Name] = i;
        foreach (var field in mapping.Fields)
        {
            if (!taken.Add(field.Column))
            {
                _log?.LogWarning("Data map → '{Table}': declared column '{Column}' collides with an earlier column — skipped.",
                    mapping.TargetTable, field.Column);
                continue;
            }
            columnIndex[field.Column] = columns.Count;
            columns.Add(new ProjectColumnSpec(field.Column, field.Type, NotNull: false, PrimaryKey: false));
        }

        var now = _clock.UtcNow.ToString("O");
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var cells in cellRows)
        {
            var row = new string?[columns.Count];
            var written = new bool[columns.Count];
            row[0] = now;
            row[1] = runId.ToString();
            written[0] = written[1] = true;
            foreach (var (_, col, text) in cells)
            {
                if (!columnIndex.TryGetValue(col, out var idx) || written[idx]) continue;
                row[idx] = text;
                written[idx] = true;
            }
            rows.Add(row);
        }

        // Pre-flight: when the target table already exists, a field pointed at a column it doesn't
        // have makes EVERY insert fail — the whole batch is dropped. Catch it up front and surface
        // a precise, actionable error instead of silently losing data on a "Succeeded" run.
        var existing = await _store.ListColumnsAsync(projectId, mapping.TargetTable, ct);
        if (existing.Count > 0)
        {
            var have = existing.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = mapping.Fields.Select(f => f.Column).Where(c => !have.Contains(c)).Distinct().ToList();
            if (missing.Count > 0)
            {
                Surface(projectId, runId, mapping.TargetTable,
                    $"{rows.Count} row(s) dropped — target column(s) {Quote(missing)} don't exist on '{mapping.TargetTable}'. " +
                    $"Its columns are: {string.Join(", ", existing.Select(c => c.Name))}. " +
                    "Point the field(s) at an existing column, or add the column on the Data tab first.");
                return;
            }
        }

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
        if (_notifier is null) return;
        var at = _clock.UtcNow;
        _notifier.Sync(new RunStatusUpdate(
            $"data-map:{runId:N}:{table}", projectId, $"Data map failed — {table}",
            RunOutcome.Failed, detail, $"/observability?run={runId}", at, at));
    }

    private static string Quote(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"'{n}'"));

    // The run's primary data: the reduce artifact (final aggregate) when present, else the lone
    // shard's artifact, else a JSON array of all shard artifacts — same shape chains thread forward.
    private static string? PrimaryArtifact(JobRun run)
    {
        if (run.ReduceResult?.Artifact is { Length: > 0 } r) return r;
        var shardArtifacts = run.ShardResults
            .OrderBy(s => s.Index)
            .Where(s => !string.IsNullOrWhiteSpace(s.Artifact))
            .Select(s => s.Artifact!)
            .ToList();
        return shardArtifacts.Count switch
        {
            0 => null,
            1 => shardArtifacts[0],
            _ => "[" + string.Join(",", shardArtifacts) + "]",
        };
    }

    // A job's stdout can carry leading noise before its JSON — pip-install logs from a runtime that
    // installs requirements, framework warnings, etc. — and some jobs print more than one JSON line.
    // Parse the whole artifact first (the clean case, unchanged); only on failure fall back to the LAST
    // line that parses as a JSON value. Jobs emit their result as a final single-line json.dumps, so the
    // last parseable line is the payload. Multi-line pretty-printed JSON behind noise still can't be
    // recovered — but nothing that parses today changes behaviour.
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
            throw; // nothing parseable — let the caller surface the original error
        }
    }

    // An array yields one record per element; a single object is one record.
    private static IEnumerable<JsonElement> Records(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                yield return item;
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            yield return el;
        }
    }

    // Dot-path navigation over objects ("data.items"). Null/empty path = the element itself.
    private static JsonElement? Navigate(JsonElement el, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return el;
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
