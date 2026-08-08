using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Data.Tests;

public sealed class SavedQueryStoreTests
{
    [Fact]
    public async Task SaveAndDelete_RoundTripsWithinDataPersistence()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<DataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new DataDbContext(options, new FakeCurrentTenant(tenantId));
        var store = new EfSavedQueryStore(context);
        var now = DateTimeOffset.UtcNow;
        var query = new SavedQueryRecord(
            Guid.NewGuid(), Guid.NewGuid(), "Recent", "select * from sales", now, now);

        await store.SaveAsync(query);

        Assert.Equal(query, await store.GetAsync(query.Id));
        Assert.Equal(tenantId, Assert.Single(context.SavedQueries.Local).TenantId);
        Assert.True(await store.DeleteAsync(query.Id));
        Assert.Null(await store.GetAsync(query.Id));
    }
}
