namespace PlaceContext.Domain.Repositories;

/// <summary>Commit boundary for Data-owned persistence.</summary>
public interface IDataUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
