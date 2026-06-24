using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Repositories;

/// <summary>Persistence of a project's single Markdown context document (one per project).</summary>
public interface IProjectContextRepository
{
    Task<ProjectContext?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default);
    Task SaveAsync(ProjectContext context, CancellationToken ct = default);
}
