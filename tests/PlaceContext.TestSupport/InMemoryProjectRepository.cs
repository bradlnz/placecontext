using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly ConcurrentDictionary<Guid, Project> _store = new();

    public Task AddAsync(Project project, CancellationToken ct = default)
    {
        _store[project.Id.Value] = project;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        _store[project.Id.Value] = project;
        return Task.CompletedTask;
    }

    public Task<Project?> GetByIdAsync(ProjectId id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id.Value));

    public Task<Project?> GetByPathAsync(RepoPath path, CancellationToken ct = default)
        => Task.FromResult(_store.Values.FirstOrDefault(p => p.Path == path));

    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Project>>(_store.Values.ToList());
}
