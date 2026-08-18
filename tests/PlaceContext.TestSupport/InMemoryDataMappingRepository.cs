using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.TestSupport;

public sealed class InMemoryDataMappingRepository : IDataMappingRepository
{
    private readonly List<DataMapping> _store = new();

    public Task AddAsync(DataMapping mapping, CancellationToken ct = default)
    {
        _store.Add(mapping);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DataMapping mapping, CancellationToken ct = default)
        => Task.CompletedTask; // reference mutated in place

    public Task DeleteAsync(Guid mappingId, CancellationToken ct = default)
    {
        _store.RemoveAll(m => m.Id == mappingId);
        return Task.CompletedTask;
    }

    public Task<DataMapping?> GetByIdAsync(Guid mappingId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(m => m.Id == mappingId));

    public Task<IReadOnlyList<DataMapping>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DataMapping>>(
            _store.Where(m => m.ProjectId == projectId).OrderBy(m => m.CreatedAt).ToList());

    public Task<IReadOnlyList<DataMapping>> ListForJobAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DataMapping>>(
            _store.Where(m => m.JobId == jobId).OrderBy(m => m.CreatedAt).ToList());
}
