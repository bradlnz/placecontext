using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaceContext.Search.Infrastructure.Persistence;

public sealed class SearchDbContextFactory : IDesignTimeDbContextFactory<SearchDbContext>
{
    public SearchDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? SearchPersistenceOptions.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Search"))
            .Options;

        return new SearchDbContext(options, new SearchDesignTimeTenant());
    }
}
