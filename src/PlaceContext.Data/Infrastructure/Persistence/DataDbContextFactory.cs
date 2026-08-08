using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Data.Infrastructure.Persistence;

public sealed class DataDbContextFactory : IDesignTimeDbContextFactory<DataDbContext>
{
    public DataDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? DataPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Data"))
            .Options;

        return new DataDbContext(options, new DataDesignTimeTenant());
    }
}
