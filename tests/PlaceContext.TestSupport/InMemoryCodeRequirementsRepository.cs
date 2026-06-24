using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>In-memory code-requirements store: one global doc (key Guid.Empty) plus per-project docs.</summary>
public sealed class InMemoryCodeRequirementsRepository : ICodeRequirementsRepository
{
    private readonly ConcurrentDictionary<Guid, CodeRequirements> _store = new();

    public Task<CodeRequirements?> GetGlobalAsync(CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(Guid.Empty));

    public Task<CodeRequirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(projectId.Value));

    public Task SaveAsync(CodeRequirements requirements, CancellationToken ct = default)
    {
        _store[requirements.ProjectId?.Value ?? Guid.Empty] = requirements;
        return Task.CompletedTask;
    }
}
