using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Vault.Infrastructure.Persistence;

public static class VaultDatabaseMigrationExtensions
{
    public static async Task MigrateVaultDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VaultDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
