using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Infrastructure.Persistence;
using PlaceContext.Search.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Search.Tests;

public sealed class OpenSearchDashboardStoreTests
{
    [Fact]
    public async Task SaveAndRead_RoundTripsEncryptedDashboardWithinTenant()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<SearchDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var encryptor = new SearchDataProtectionEncryptor(new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow;
        var record = new OpenSearchDashboardRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Active jobs",
            "jobs-*",
            "status:active",
            "status",
            "terms",
            "bar",
            "count",
            null,
            null,
            "{\"title\":\"Active jobs\"}",
            now,
            now);

        await using var context = new SearchDbContext(options, new FakeCurrentTenant(tenantId));
        var store = new EfOpenSearchDashboardStore(context, encryptor);
        await store.SaveAsync(record);

        var stored = Assert.Single(context.OpenSearchDashboards.Local);
        Assert.Equal(tenantId, stored.TenantId);
        Assert.True(encryptor.IsProtected(stored.QueryText));
        Assert.True(encryptor.IsProtected(stored.ChartSpecJson));

        var loaded = await store.GetAsync(record.Id);
        Assert.Equal(record, loaded);
    }
}
