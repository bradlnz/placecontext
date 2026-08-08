using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentsDbContextFactory : IDesignTimeDbContextFactory<AgentsDbContext>
{
    public AgentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault() ?? AgentsPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<AgentsDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Agents"))
            .Options;
        return new AgentsDbContext(options, new AgentsDesignTimeTenant());
    }
}
