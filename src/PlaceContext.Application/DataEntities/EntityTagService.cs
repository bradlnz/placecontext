using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>One edge of the run↔entity relation tree: this run's output carried this entity key.</summary>
public sealed record EntityTag(Guid ProjectId, Guid EntityId, string EntityName, string Key, Guid RunId, Guid JobId);

/// <summary>A persisted tag edge, as consumed by graph views: key value ⇄ run (and its job).</summary>
public sealed record EntityTagPair(string Key, Guid RunId, Guid JobId);

/// <summary>Persistence port for entity tags (a link store, not an aggregate — like the tool-call log).</summary>
public interface IEntityTagStore
{
    /// <summary>Insert tags, silently skipping ones that already exist for (entity, key, run).</summary>
    Task AddAsync(IReadOnlyList<EntityTag> tags, CancellationToken ct = default);

    /// <summary>Run ids whose output was tagged with this entity key (newest first).</summary>
    Task<IReadOnlyList<Guid>> RunsForKeyAsync(Guid entityId, string key, int take = 20, CancellationToken ct = default);

    /// <summary>All run ids tagged against this entity, any key (newest first).</summary>
    Task<IReadOnlyList<Guid>> RunsForEntityAsync(Guid entityId, int take = 20, CancellationToken ct = default);

    /// <summary>The tag pairs for an entity — which key value each run was linked through.</summary>
    Task<IReadOnlyList<EntityTagPair>> PairsForEntityAsync(Guid entityId, int take = 60, CancellationToken ct = default);
}

/// <summary>
/// Builds the relation tree automatically: after a run completes, its primary JSON artifact's
/// string values are matched against each entity's key values (the label + relation columns of the
/// tagged table, read as the project's isolated role). A match — say, an address that exists on the
/// sites entity — persists a tag linking run ⇄ entity record, which transitively links the job and
/// the run's artifacts to that record. Entirely best-effort and capped; tagging never fails a run.
/// </summary>
public sealed class EntityTagService
{
    private const int MaxArtifactValues = 400;
    private const int MaxKeysPerEntity = 1000;

    private readonly IDataEntityRepository _entities;
    private readonly IProjectDataStore _store;
    private readonly IEntityTagStore _tags;
    private readonly ILogger<EntityTagService>? _log;

    public EntityTagService(IDataEntityRepository entities, IProjectDataStore store, IEntityTagStore tags,
        ILogger<EntityTagService>? log = null)
    {
        _entities = entities;
        _store = store;
        _tags = tags;
        _log = log;
    }

    public async Task TagRunAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        try
        {
            var artifactValues = CollectStrings(run);
            if (artifactValues.Count == 0) return;

            var entities = await _entities.ListForProjectAsync(run.ProjectId, ct);
            if (entities.Count == 0) return;

            var found = new List<EntityTag>();
            foreach (var entity in entities)
            {
                foreach (var column in KeyColumns(entity))
                {
                    IReadOnlyList<IReadOnlyList<string?>> rows;
                    try
                    {
                        var r = await _store.ExecuteAsync(run.ProjectId,
                            $"SELECT DISTINCT \"{column.Replace("\"", "")}\"::text FROM \"{entity.TableName.Replace("\"", "")}\" LIMIT {MaxKeysPerEntity}", ct);
                        rows = r.Rows;
                    }
                    catch { continue; } // column/table missing — the tag pass skips, never fails

                    foreach (var row in rows)
                    {
                        if (row.Count == 0 || row[0] is not { Length: > 2 } key) continue;
                        if (artifactValues.Contains(key))
                            found.Add(new EntityTag(run.ProjectId, entity.Id, entity.Name, key, run.Id, job.Id));
                    }
                }
            }

            if (found.Count > 0)
            {
                await _tags.AddAsync(found.DistinctBy(t => (t.EntityId, t.Key)).ToList(), ct);
                _log?.LogInformation("Tagged run {RunId} with {Count} entity key(s).", run.Id, found.Count);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Entity tagging failed for run {RunId} — the run itself is unaffected.", run.Id);
        }
    }

    // The values that identify a record: the label column plus every relation key column.
    private static IEnumerable<string> KeyColumns(DataEntity entity)
    {
        if (entity.LabelColumn is { Length: > 0 } label) yield return label;
        foreach (var rel in entity.Relations)
            yield return rel.Column;
    }

    // Every string value in the run's primary artifact JSON (capped) — what the run "mentioned".
    private static HashSet<string> CollectStrings(JobRun run)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var primary = run.ReduceResult?.Artifact
            ?? run.ShardResults.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Artifact))?.Artifact;
        if (string.IsNullOrWhiteSpace(primary)) return values;
        try
        {
            using var doc = JsonDocument.Parse(primary);
            Walk(doc.RootElement, values);
        }
        catch { /* not JSON — nothing to tag against */ }
        return values;

        static void Walk(JsonElement el, HashSet<string> into)
        {
            if (into.Count >= MaxArtifactValues) return;
            switch (el.ValueKind)
            {
                case JsonValueKind.String when el.GetString() is { Length: > 2 } s:
                    into.Add(s);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject()) Walk(p.Value, into);
                    break;
                case JsonValueKind.Array:
                    foreach (var i in el.EnumerateArray()) Walk(i, into);
                    break;
            }
        }
    }
}

/// <summary>Runs whose output was tagged with this entity key — the relation tree, queried.</summary>
public sealed record TaggedRunsQuery(Guid EntityId, string Key) : Cqrs.IQuery<IReadOnlyList<Guid>>;

public sealed class TaggedRunsHandler : Cqrs.IQueryHandler<TaggedRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public TaggedRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(TaggedRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForKeyAsync(query.EntityId, query.Key);
}

/// <summary>Every run tagged against an entity — the section-level rollup of its relation tree.</summary>
public sealed record EntityRunsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<Guid>>;

public sealed class EntityRunsHandler : Cqrs.IQueryHandler<EntityRunsQuery, IReadOnlyList<Guid>>
{
    private readonly IEntityTagStore _tags;

    public EntityRunsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<Guid>> HandleAsync(EntityRunsQuery query, CancellationToken ct = default)
        => _tags.RunsForEntityAsync(query.EntityId);
}

/// <summary>The entity's tag pairs — the concrete edges between its records and runs.</summary>
public sealed record EntityTagPairsQuery(Guid EntityId) : Cqrs.IQuery<IReadOnlyList<EntityTagPair>>;

public sealed class EntityTagPairsHandler : Cqrs.IQueryHandler<EntityTagPairsQuery, IReadOnlyList<EntityTagPair>>
{
    private readonly IEntityTagStore _tags;

    public EntityTagPairsHandler(IEntityTagStore tags) => _tags = tags;

    public Task<IReadOnlyList<EntityTagPair>> HandleAsync(EntityTagPairsQuery query, CancellationToken ct = default)
        => _tags.PairsForEntityAsync(query.EntityId);
}
