using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>In-memory requirements store: one global doc (key Guid.Empty) plus per-project docs.</summary>
public sealed class InMemoryRequirementsRepository : IRequirementsRepository
{
    private readonly ConcurrentDictionary<Guid, Requirements> _store = new();

    public Task<Requirements?> GetGlobalAsync(CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(Guid.Empty));

    public Task<Requirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(projectId.Value));

    public Task SaveAsync(Requirements requirements, CancellationToken ct = default)
    {
        _store[requirements.ProjectId?.Value ?? Guid.Empty] = requirements;
        return Task.CompletedTask;
    }
}
