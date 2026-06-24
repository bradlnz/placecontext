using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryDecisionRepository : IDecisionRepository
{
    private readonly List<Decision> _store = new();

    public Task AddAsync(Decision decision, CancellationToken ct = default)
    {
        _store.Add(decision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Decision>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Decision>>(
            _store.Where(d => d.ProjectId == projectId).OrderByDescending(d => d.DecidedAt).ToList());
}
