using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Projects.Infrastructure.Persistence;

public sealed class ProjectsDbContextFactory : IDesignTimeDbContextFactory<ProjectsDbContext>
{
    public ProjectsDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? ProjectsPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Projects"))
            .Options;

        return new ProjectsDbContext(options, new ProjectsDesignTimeTenant());
    }
}
