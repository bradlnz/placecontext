using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DecisionTreeProvider : IDecisionTreeProvider
{
    private readonly IProjectRepository _projects;
    private readonly IChangeLedgerRepository _ledgers;
    private readonly IDecisionRepository _decisions;
    private readonly IToolCallLog _log;
    private readonly DecisionTreeAssembler _assembler;

    public DecisionTreeProvider(
        IProjectRepository projects, IChangeLedgerRepository ledgers, IDecisionRepository decisions,
        IToolCallLog log, DecisionTreeAssembler assembler)
    {
        _projects = projects;
        _ledgers = ledgers;
        _decisions = decisions;
        _log = log;
        _assembler = assembler;
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

        return _assembler.Assemble(project.Name, decisions, ledger, activity);
    }
}
