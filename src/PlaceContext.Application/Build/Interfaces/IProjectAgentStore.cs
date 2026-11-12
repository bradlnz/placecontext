namespace PlaceContext.Application.Build;

public interface IProjectAgentStore
{
    Task<ProjectAgentCatalog> GetAsync(Guid projectId, CancellationToken ct = default);
    Task SaveAsync(Guid projectId, ProjectAgentCatalog catalog, CancellationToken ct = default);
}
