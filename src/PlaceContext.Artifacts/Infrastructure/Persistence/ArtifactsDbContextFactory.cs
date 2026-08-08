using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Artifacts.Infrastructure.Persistence;

public sealed class ArtifactsDbContextFactory : IDesignTimeDbContextFactory<ArtifactsDbContext>
{
    public ArtifactsDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? ArtifactsPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<ArtifactsDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Artifacts"))
            .Options;

        return new ArtifactsDbContext(options, new ArtifactsDesignTimeTenant());
    }
}
