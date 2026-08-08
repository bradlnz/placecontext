namespace PlaceContext.Crm.Domain.Persistence;

/// <summary>Commit boundary for CRM-owned persistence.</summary>
public interface ICrmUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
