namespace PlaceContext.Domain.Repositories;

/// <summary>Commit boundary for Search-owned persistence.</summary>
public interface ISearchUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
