namespace PlaceContext.Domain.Repositories;

/// <summary>Commits one Artifacts persistence transaction.</summary>
public interface IArtifactsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
