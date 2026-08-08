using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public sealed class AgentChatDbContextFactory : IDesignTimeDbContextFactory<AgentChatDbContext>
{
    public AgentChatDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? AgentChatPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<AgentChatDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_AgentChat"))
            .Options;

        return new AgentChatDbContext(options, new AgentChatDesignTimeTenant());
    }
}
