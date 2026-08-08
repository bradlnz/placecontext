using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Data.Infrastructure.Persistence;

public static class DataDatabaseMigrationExtensions
{
    public static async Task MigrateDataDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
