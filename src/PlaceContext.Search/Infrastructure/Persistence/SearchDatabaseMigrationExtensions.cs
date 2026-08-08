using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Search.Infrastructure.Persistence;

public static class SearchDatabaseMigrationExtensions
{
    public static async Task MigrateSearchDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
