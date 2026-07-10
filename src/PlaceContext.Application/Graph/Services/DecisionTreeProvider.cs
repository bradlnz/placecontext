using PlaceContext.Application.Ports;
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

    public DecisionTreeProvider(
        IProjectRepository projects, IActivityLogRepository ledgers, IDecisionRepository decisions,
        IToolCallLog log, DecisionTreeAssembler assembler,
        // Optional: when the embedding store is present, embedded run outputs are woven into the graph
        // as semantically-linked "brain" nodes. The graph still assembles fully without it.
        IRunEmbeddingRepository? runOutputs = null)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _log = log;
        _assembler = assembler;
        _runOutputs = runOutputs;
    }

    public async Task<DecisionTree> BuildAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId.Value} not found.");

        var ledger = await _ledgers.GetForProjectAsync(projectId, ct);
        var decisions = await _decisions.ListForProjectAsync(projectId, ct);

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
                .Select(e => new RunOutputNode(e.JobRunId.ToString("N")[..8], e.Text, e.Vector))
                .ToList();
        }

        return _assembler.Assemble(project.Name, decisions, ledger, activity, runOutputs);
    }
}
