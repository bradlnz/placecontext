using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence port for <see cref="DataEntity"/> aggregates (the business-entity registry).</summary>
public interface IDataEntityRepository
{
    Task AddAsync(DataEntity entity, CancellationToken ct = default);
    Task UpdateAsync(DataEntity entity, CancellationToken ct = default);
    Task DeleteAsync(Guid entityId, CancellationToken ct = default);
    Task<DataEntity?> GetByIdAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<DataEntity>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
}
