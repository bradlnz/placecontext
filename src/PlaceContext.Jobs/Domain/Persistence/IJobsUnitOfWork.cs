namespace PlaceContext.Jobs.Domain.Persistence;

/// <summary>Commit boundary for Jobs-owned persistence.</summary>
public interface IJobsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
