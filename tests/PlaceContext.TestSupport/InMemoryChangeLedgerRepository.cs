using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class InMemoryChangeLedgerRepository : IChangeLedgerRepository
{
    private readonly ConcurrentDictionary<Guid, ChangeLedger> _store = new();

    public Task<ChangeLedger> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(projectId.Value) ?? ChangeLedger.Start(projectId));

    public Task SaveAsync(ChangeLedger ledger, CancellationToken ct = default)
    {
        _store[ledger.ProjectId.Value] = ledger;
        return Task.CompletedTask;
    }
}
