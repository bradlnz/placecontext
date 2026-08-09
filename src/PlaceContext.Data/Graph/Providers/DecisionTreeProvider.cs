using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Data.Integration;

namespace PlaceContext.Application.Features;

public sealed class DecisionTreeProvider : IDecisionTreeProvider, IUncachedDecisionTreeProvider
{
    private readonly IProjectRepository _projects;
    private readonly IActivityLogRepository _ledgers;
    private readonly IDecisionRepository _decisions;
    private readonly DecisionTreeAssembler _assembler;
    private readonly IDataJobsClient _jobs;
    private readonly IDataMappingRepository _mappings;
    private readonly IProjectDataStore _projectData;
    private readonly IDataEntityRepository _entities;
    private readonly IRecordLinkStore? _links;

    public DecisionTreeProvider(
        IProjectRepository projects, IActivityLogRepository ledgers, IDecisionRepository decisions,
        DecisionTreeAssembler assembler,
        IDataJobsClient jobs, IDataMappingRepository mappings,
        IProjectDataStore projectData, IDataEntityRepository entities,
        IRecordLinkStore? links = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _assembler = assembler;
        _jobs = jobs;
        _mappings = mappings;
        _projectData = projectData;
        _entities = entities;
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
        var catalog = await _jobs.GetCatalogAsync(projectId.Value, ct);
        var jobs = catalog.Jobs;
        var chains = catalog.Chains;
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

        IReadOnlyList<ToolActivity> activity = Array.Empty<ToolActivity>();
        IReadOnlyList<RunOutputNode> runOutputs = Array.Empty<RunOutputNode>();

        // Entity-aligned runtime data: recent job runs, their artifacts, and shared record-link values
        // (addresses/locations, emails, phones, etc.). Best-effort — the graph still assembles if any
        // store is unavailable or empty.
        IReadOnlyList<DataRunSummary> runs = catalog.Runs;
        IReadOnlyList<DataArtifactSummary> artifacts = Array.Empty<DataArtifactSummary>();
        IReadOnlyList<RecordLinkCluster> linkClusters = Array.Empty<RecordLinkCluster>();

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
