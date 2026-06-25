using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of the append-only change ledger for a project.</summary>
public interface IActivityLogRepository
{
    Task<ActivityLog> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task SaveAsync(ActivityLog ledger, CancellationToken ct = default);
}
