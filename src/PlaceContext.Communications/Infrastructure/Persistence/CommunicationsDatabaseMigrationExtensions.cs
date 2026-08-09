using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Communications.Infrastructure.Persistence;

public static class CommunicationsDatabaseMigrationExtensions
{
    public static async Task MigrateCommunicationsDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>()
            .Database.MigrateAsync();
    }
}
