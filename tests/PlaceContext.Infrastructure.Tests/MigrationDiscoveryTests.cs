using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void AllowApiInvocation_migration_is_discovered()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=placecontext;Username=placecontext;Password=unused")
            .Options;

        using var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid()));

        Assert.Contains("20260805070000_AddJobAllowApiInvocation", db.Database.GetMigrations());
    }
}
