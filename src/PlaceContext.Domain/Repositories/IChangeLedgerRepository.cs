using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of the append-only change ledger for a project.</summary>
public interface IChangeLedgerRepository
{
    Task<ChangeLedger> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task SaveAsync(ChangeLedger ledger, CancellationToken ct = default);
}
