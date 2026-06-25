using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Collection-like contract for project registry records. Port declared here, implemented in Infrastructure.</summary>
public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken ct = default);
    Task UpdateAsync(Project project, CancellationToken ct = default);
    Task<Project?> GetByIdAsync(ProjectId id, CancellationToken ct = default);
    Task<Project?> GetByPathAsync(ProjectPath path, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default);
}
