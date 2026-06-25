using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryActivityLogRepository : IActivityLogRepository
{
    private readonly ConcurrentDictionary<Guid, ActivityLog> _store = new();

    public Task<ActivityLog> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(projectId.Value) ?? ActivityLog.Start(projectId));

    public Task SaveAsync(ActivityLog ledger, CancellationToken ct = default)
    {
        _store[ledger.ProjectId.Value] = ledger;
        return Task.CompletedTask;
    }
}
