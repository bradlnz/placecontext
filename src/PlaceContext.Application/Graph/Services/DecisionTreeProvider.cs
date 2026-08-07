using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DecisionTreeProvider : IDecisionTreeProvider
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IDecisionRepository _decisions;
    private readonly IToolCallLog _log;
    private readonly DecisionTreeAssembler _assembler;
    private readonly IRunEmbeddingRepository? _runOutputs;
    private readonly IJobRepository _jobs;
    private readonly IJobChainRepository _chains;
    private readonly IDataMappingRepository _mappings;
    private readonly IProjectDataStore _projectData;
    private readonly IDataEntityRepository _entities;
    private readonly IJobRunRepository? _runs;
    private readonly IRunArtifactLinkRepository? _artifacts;
    private readonly IRecordLinkStore? _links;

    public DecisionTreeProvider(
        IProjectRepository projects, IActivityLogRepository ledgers, IDecisionRepository decisions,
        IToolCallLog log, DecisionTreeAssembler assembler,
        IJobRepository jobs, IJobChainRepository chains, IDataMappingRepository mappings,
        IProjectDataStore projectData, IDataEntityRepository entities,
        // Optional: when the embedding store is present, embedded run outputs are woven into the graph
        // as semantically-linked "brain" nodes. The graph still assembles fully without it.
        IRunEmbeddingRepository? runOutputs = null,
        IJobRunRepository? runs = null,
        IRunArtifactLinkRepository? artifacts = null,
        IRecordLinkStore? links = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _log = log;
        _assembler = assembler;
        _jobs = jobs;
        _chains = chains;
        _mappings = mappings;
        _projectData = projectData;
        _entities = entities;
        _runOutputs = runOutputs;
        _runs = runs;
        _artifacts = artifacts;
        _links = links;
    }

    public async Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId.Value} not found.");

        var ledger = await _ledgers.GetForProjectAsync(projectId, ct);
        var decisions = await _decisions.ListForProjectAsync(projectId, ct);

        // Structural lineage: jobs, the chains they belong to, the tables they write (data mappings),
        // and the project's tables. This is the dependency graph of the data platform itself.
        var jobs = await _jobs.ListForProjectAsync(projectId.Value, ct);
        var chains = await _chains.ListForProjectAsync(projectId.Value, ct);
        var mappings = await _mappings.ListForProjectAsync(projectId.Value, ct);
        var entities = await _entities.ListForProjectAsync(projectId.Value, ct);
        IReadOnlyList<string> tables = Array.Empty<string>();
        try
        {
            tables = (await _projectData.ListTablesAsync(projectId.Value, ct)).Select(t => t.Name).ToList();
        }
        catch
        {
            // Project data store may be unavailable/unprovisioned — the graph still assembles without it.
        }

        // The tool-call log is shared across projects; keep only this project's entries.
        var key = projectId.Value.ToString();
        var activity = _log.Recent(200)
            .Where(e => string.Equals(e.Project, key, StringComparison.OrdinalIgnoreCase))
            .Select(e => new ToolActivity(e.Tool, e.Status == ToolCallStatus.Error))
            .ToList();

        // Embedded run outputs (if the vector store is wired) become the project's "brain": the assembler
        // weaves them in as nodes and cross-links the semantically-nearest ones. Best-effort — a store that
        // is unavailable or empty simply yields no run-output nodes.
        IReadOnlyList<RunOutputNode> runOutputs = Array.Empty<RunOutputNode>();
        if (_runOutputs is not null)
        {
            var embeddings = await _runOutputs.ListForProjectAsync(projectId.Value, ct: ct);
            runOutputs = embeddings
                .Where(e => e.Vector.Length > 0)
                .Select(e => new RunOutputNode(e.JobRunId.ToString("N")[..8], e.Text, e.Vector, e.JobId))
                .ToList();
        }

        // Entity-aligned runtime data: recent job runs, their artifacts, and shared record-link values
        // (addresses/locations, emails, phones, etc.). Best-effort — the graph still assembles if any
        // store is unavailable or empty.
        IReadOnlyList<JobRun> runs = Array.Empty<JobRun>();
        IReadOnlyList<RunArtifactLink> artifacts = Array.Empty<RunArtifactLink>();
        IReadOnlyList<RecordLinkCluster> linkClusters = Array.Empty<RecordLinkCluster>();

        try
        {
            if (_runs is not null)
            {
                var allRuns = new List<JobRun>();
                foreach (var job in jobs)
                    allRuns.AddRange(await _runs.ListForJobAsync(job.Id, ct));
                runs = allRuns.OrderByDescending(r => r.StartedAt).Take(60).ToList();
            }
        }
        catch { }

        try
        {
            artifacts = _artifacts is not null
                ? await _artifacts.ListForProjectAsync(projectId.Value, take: 200, ct: ct)
                : Array.Empty<RunArtifactLink>();
        }
        catch { }

        try
        {
            if (_links is not null)
            {
                var groups = await _links.GroupsAsync(projectId.Value, take: 100, ct);
                linkClusters = groups.Select(g => new RecordLinkCluster(
                    g.Kind,
                    g.NormalizedValue,
                    g.DisplayValue,
                    g.Occurrences.Select(o => new RecordLinkClusterOccurrence(o.TableName, o.ColumnName, o.RowKey)).ToList()
                )).ToList();
            }
        }
        catch { }

        return _assembler.Assemble(project.Name, decisions, ledger, activity, runOutputs,
            jobs, chains, mappings, tables, entities, runs, artifacts, linkClusters);
    }
}
