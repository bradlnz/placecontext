using Microsoft.EntityFrameworkCore;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class EfTenantCatalogTests
{
    [Fact]
    public async Task Lists_and_finds_tenants_without_exposing_persistence_rows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, new FakeCurrentTenant(firstId));
        db.Tenants.AddRange(
            new TenantRow
            {
                Id = firstId,
                Slug = "first",
                Name = "First",
                TimeZoneId = "Australia/Brisbane",
            },
            new TenantRow
            {
                Id = secondId,
                Slug = "second",
                Name = "Second",
                TimeZoneId = "UTC",
            });
        await db.SaveChangesAsync();
        var catalog = new EfTenantCatalog(db);

        var tenants = await catalog.ListAsync();
        var second = await catalog.FindAsync(secondId);

        Assert.Equal(2, tenants.Count);
        Assert.Equal("second", second?.Slug);
        Assert.Equal("UTC", second?.TimeZoneId);
        Assert.Null(await catalog.FindAsync(Guid.NewGuid()));
    }
}
