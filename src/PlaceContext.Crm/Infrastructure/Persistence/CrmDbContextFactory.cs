using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Crm.Infrastructure.Persistence;

public sealed class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? CrmPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Crm"))
            .Options;

        return new CrmDbContext(options, new CrmDesignTimeTenant());
    }
}
