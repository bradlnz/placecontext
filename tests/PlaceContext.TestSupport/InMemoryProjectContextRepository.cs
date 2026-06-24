using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryProjectContextRepository : IProjectContextRepository
{
    private readonly ConcurrentDictionary<Guid, ProjectContext> _store = new();

    public Task<ProjectContext?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(projectId.Value));

    public Task SaveAsync(ProjectContext context, CancellationToken ct = default)
    {
        _store[context.ProjectId.Value] = context;
        return Task.CompletedTask;
    }
}
