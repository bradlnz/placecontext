using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Vault.Tests;

internal sealed class RecordingVaultUnitOfWork : IVaultUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}
