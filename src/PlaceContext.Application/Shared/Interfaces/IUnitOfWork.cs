namespace PlaceContext.Application.Shared;

/// <summary>Commit boundary for a unit of work across the repositories.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
