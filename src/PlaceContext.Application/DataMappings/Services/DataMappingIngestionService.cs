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
/// project database. Tables are system-owned append-only (created on first ingest) with
/// <c>ingested_at</c>/<c>run_id</c> provenance columns, so lineage back to the run is always
/// queryable. Entirely best-effort — a mapping failure is logged and never fails the run.
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
    private readonly ILogger<DataMappingIngestionService>? _log;

    public DataMappingIngestionService(
        IDataMappingRepository mappings,
        IProjectDataStore store,
        IClock clock,
        IContentIndexer? indexer = null,
        ILogger<DataMappingIngestionService>? log = null)
    {
        _mappings = mappings;
        _store = store;
        _clock = clock;
        _indexer = indexer;
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
                _log?.LogWarning(ex, "Data mapping {MappingId} → table '{Table}' failed for run {RunId}.",
                    mapping.Id, mapping.TargetTable, run.Id);
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
                _log?.LogWarning(ex, "Chain data mapping {MappingId} → table '{Table}' failed for run {RunId}.",
                    mapping.Id, mapping.TargetTable, chainRunId);
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
        using var doc = JsonDocument.Parse(primary);
        var root = Navigate(doc.RootElement, mapping.RowsPath);
        if (root is not { } records) return;

        var columns = ProvenanceColumns
            .Concat(mapping.Fields.Select(f => new ProjectColumnSpec(f.Column, f.Type, NotNull: false, PrimaryKey: false)))
            .ToList();

        var now = _clock.UtcNow.ToString("O");
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var record in Records(records))
        {
            var row = new string?[columns.Count];
            row[0] = now;
            row[1] = runId.ToString();
            for (var i = 0; i < mapping.Fields.Count; i++)
                row[i + 2] = ValueText(Navigate(record, mapping.Fields[i].SourcePath));
            rows.Add(row);
        }
        if (rows.Count == 0) return;

        await _store.AppendReadOnlyRowsAsync(projectId, mapping.TargetTable, columns, rows, ct);
        _log?.LogInformation("Ingested {Count} row(s) from run {RunId} into '{Table}'.",
            rows.Count, runId, mapping.TargetTable);

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

    // Values travel as text and are cast server-side to the column's declared type.
    private static string? ValueText(JsonElement? el) => el is not { } v ? null : v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.GetRawText(), // objects/arrays land as their JSON text
    };
}
