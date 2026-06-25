using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of requirements documents: one global, plus one per project.</summary>
public interface IRequirementsRepository
{
    Task<Requirements?> GetGlobalAsync(CancellationToken ct = default);
    Task<Requirements?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task SaveAsync(Requirements requirements, CancellationToken ct = default);
}
