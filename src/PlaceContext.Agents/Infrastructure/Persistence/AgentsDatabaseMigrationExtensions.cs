using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Agents.Infrastructure.Persistence;

public static class AgentsDatabaseMigrationExtensions
{
    public static async Task MigrateAgentsDatabaseAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgentsDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
