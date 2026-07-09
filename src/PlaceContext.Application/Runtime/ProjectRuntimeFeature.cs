using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>The containers a project's application runtime is running (DinD in-cluster).</summary>
public sealed record ListProjectContainersQuery(Guid ProjectId) : IQuery<IReadOnlyList<ContainerInfo>>;

/// <summary>The log tail of one of the project's containers.</summary>
public sealed record GetContainerLogsQuery(Guid ProjectId, string ContainerId, int Tail = 200) : IQuery<string>;

/// <summary>Restart one of the project's containers.</summary>
public sealed record RestartProjectContainerCommand(Guid ProjectId, string ContainerId) : ICommand<bool>;

/// <summary>Stop one of the project's containers.</summary>
public sealed record StopProjectContainerCommand(Guid ProjectId, string ContainerId) : ICommand<bool>;

public sealed class ListProjectContainersHandler : IQueryHandler<ListProjectContainersQuery, IReadOnlyList<ContainerInfo>>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    public ListProjectContainersHandler(IProjectRepository projects, IContainerRuntime runtime) { _projects = projects; _runtime = runtime; }

    public async Task<IReadOnlyList<ContainerInfo>> HandleAsync(ListProjectContainersQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return _runtime.IsEnabled
            ? await _runtime.ListAsync(q.ProjectId, ct)
            : Array.Empty<ContainerInfo>();
    }
}

public sealed class GetContainerLogsHandler : IQueryHandler<GetContainerLogsQuery, string>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    public GetContainerLogsHandler(IProjectRepository projects, IContainerRuntime runtime) { _projects = projects; _runtime = runtime; }

    public async Task<string> HandleAsync(GetContainerLogsQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _runtime.LogsAsync(q.ProjectId, q.ContainerId, Math.Clamp(q.Tail, 10, 2000), ct);
    }
}

public sealed class RestartProjectContainerHandler : ICommandHandler<RestartProjectContainerCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    public RestartProjectContainerHandler(IProjectRepository projects, IContainerRuntime runtime) { _projects = projects; _runtime = runtime; }

    public async Task<bool> HandleAsync(RestartProjectContainerCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _runtime.RestartAsync(c.ProjectId, c.ContainerId, ct);
        return true;
    }
}

public sealed class StopProjectContainerHandler : ICommandHandler<StopProjectContainerCommand, bool>
{
    private readonly IProjectRepository _projects;
    private readonly IContainerRuntime _runtime;
    public StopProjectContainerHandler(IProjectRepository projects, IContainerRuntime runtime) { _projects = projects; _runtime = runtime; }

    public async Task<bool> HandleAsync(StopProjectContainerCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        await _runtime.StopAsync(c.ProjectId, c.ContainerId, ct);
        return true;
    }
}
