using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of architecture decisions.</summary>
public interface IDecisionRepository
{
    Task AddAsync(Decision decision, CancellationToken ct = default);
    Task<IReadOnlyList<Decision>> ListForProjectAsync(ProjectId projectId, CancellationToken ct = default);
}
