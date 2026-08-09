using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Communications.Infrastructure.Persistence;

public sealed class CommunicationsDbContextFactory : IDesignTimeDbContextFactory<CommunicationsDbContext>
{
    public CommunicationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CommunicationsDbContext>()
            .UseNpgsql(
                CommunicationsPersistenceOptions.DefaultConnectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory_Communications"))
            .Options;
        return new CommunicationsDbContext(options, new CommunicationsDesignTimeTenant());
    }
}
