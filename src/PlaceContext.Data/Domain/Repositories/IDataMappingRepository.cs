using PlaceContext.Domain.Entities;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence port for <see cref="DataMapping"/> aggregates (the project data map).</summary>
public interface IDataMappingRepository
{
    Task AddAsync(DataMapping mapping, CancellationToken ct = default);
    Task UpdateAsync(DataMapping mapping, CancellationToken ct = default);
    Task DeleteAsync(Guid mappingId, CancellationToken ct = default);
    Task<DataMapping?> GetByIdAsync(Guid mappingId, CancellationToken ct = default);
    Task<IReadOnlyList<DataMapping>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<DataMapping>> ListForJobAsync(Guid jobId, CancellationToken ct = default);
}
