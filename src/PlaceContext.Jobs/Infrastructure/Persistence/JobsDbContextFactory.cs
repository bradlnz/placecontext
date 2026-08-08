using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

public sealed class JobsDbContextFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? JobsPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Jobs"))
            .Options;

        return new JobsDbContext(options, new JobsDesignTimeTenant());
    }
}
