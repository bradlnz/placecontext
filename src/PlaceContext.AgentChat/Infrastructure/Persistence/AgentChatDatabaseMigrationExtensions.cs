using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.AgentChat.Infrastructure.Persistence;

public static class AgentChatDatabaseMigrationExtensions
{
    public static async Task MigrateAgentChatDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AgentChatDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
