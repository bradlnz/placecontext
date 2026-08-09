using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Graph;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class RebuildGraphHandler : ICommandHandler<RebuildGraphCommand, GraphRebuildResult>
{
    private readonly IProjectRepository _projects;
    private readonly IDecisionTreeProvider _tree;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public RebuildGraphHandler(
        IProjectRepository projects, IDecisionTreeProvider tree, IUnitOfWork uow, IClock clock)
    {
        _projects = projects;
        _tree = tree;
        _uow = uow;
        _clock = clock;
    }

    public async Task<GraphRebuildResult> HandleAsync(RebuildGraphCommand command, CancellationToken ct = default)
    {
        var project = await _projects.GetByIdAsync(ProjectId.From(command.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {command.ProjectId} not found.");

        // An explicit rebuild must see brand-new data — drop any cached tree first.
        _tree.Invalidate(project.Id);
        var tree = await _tree.BuildAsync(project.Id, ct);
        var snapshot = GraphSnapshotRef.Of(
            $"decision-tree:{project.Id.Value}", _clock.UtcNow,
            tree.Nodes.Count, tree.Edges.Count, tree.Hotspots());

        project.RecordGraphBuild(snapshot);
        await _projects.UpdateAsync(project, ct);
        await _uow.SaveChangesAsync(ct);
        return new GraphRebuildResult(
            project.Id.Value,
            project.Name.Value,
            project.Path.Value,
            project.Status.ToString(),
            project.IsGraphified,
            project.LastGraph?.GodNodes.Count ?? 0,
            project.LastGraph?.NodeCount ?? 0,
            project.LastGraph?.LinkCount ?? 0);
    }
}
