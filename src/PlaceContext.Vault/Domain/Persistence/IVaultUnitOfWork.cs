namespace PlaceContext.Vault.Domain.Repositories;

/// <summary>Commits a single Vault persistence transaction.</summary>
public interface IVaultUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
