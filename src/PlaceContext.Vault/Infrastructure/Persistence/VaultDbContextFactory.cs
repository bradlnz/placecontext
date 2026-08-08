using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Vault.Infrastructure.Persistence;

public sealed class VaultDbContextFactory : IDesignTimeDbContextFactory<VaultDbContext>
{
    public VaultDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? VaultPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<VaultDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Vault"))
            .Options;

        return new VaultDbContext(options, new VaultDesignTimeTenant());
    }
}
