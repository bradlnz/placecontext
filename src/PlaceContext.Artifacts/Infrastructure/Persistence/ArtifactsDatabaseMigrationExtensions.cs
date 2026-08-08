using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Artifacts.Infrastructure.Persistence;

public static class ArtifactsDatabaseMigrationExtensions
{
    public static async Task MigrateArtifactsDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ArtifactsDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
