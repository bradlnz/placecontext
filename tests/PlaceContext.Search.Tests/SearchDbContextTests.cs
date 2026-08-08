using Microsoft.EntityFrameworkCore;
using PlaceContext.Search.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Search.Tests;

public sealed class SearchDbContextTests
{
    [Fact]
    public async Task SaveChangesAsync_UnstampedDashboard_StampsTenantAndIsolatesReads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var owner = new SearchDbContext(options, new FakeCurrentTenant(tenantId)))
        {
            owner.OpenSearchDashboards.Add(new OpenSearchDashboardRow
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                Name = "Activity",
                BucketField = "status",
                ChartSpecJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            await owner.SaveChangesAsync();

            Assert.Equal(tenantId, Assert.Single(owner.OpenSearchDashboards.Local).TenantId);
        }

        await using var otherTenant = new SearchDbContext(
            options,
            new FakeCurrentTenant(Guid.NewGuid()));
        Assert.Empty(await otherTenant.OpenSearchDashboards.ToListAsync());
    }
}
