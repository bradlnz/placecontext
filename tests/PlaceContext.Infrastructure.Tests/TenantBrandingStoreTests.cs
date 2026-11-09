using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class TenantBrandingStoreTests
{
    [Fact]
    public async Task Branding_commands_commit_only_the_current_tenant_and_reset_to_defaults()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        var other = new TenantRow { Id = Guid.NewGuid(), BrandingJson = "{\"ProductName\":\"Other\"}" };
        db.Tenants.AddRange(new TenantRow { Id = tenant.TenantId }, other);
        await db.SaveChangesAsync();
        var store = new EfTenantBrandingStore(db, tenant);
        var read = new GetTenantBrandingHandler(store);
        var write = new SetTenantBrandingHandler(store, db);
        var brand = new TenantBranding(ProductName: "Example", AccentColor: "#123456");

        await write.HandleAsync(new SetTenantBrandingCommand(brand));
        db.ChangeTracker.Clear();
        Assert.Equal(brand, await read.HandleAsync(new GetTenantBrandingQuery()));
        Assert.Equal(other.BrandingJson, (await db.Tenants.FindAsync(other.Id))!.BrandingJson);
        Assert.Equal(Permission.SettingsManage, new SetTenantBrandingCommand(brand).RequiredPermission);

        await write.HandleAsync(new SetTenantBrandingCommand(new TenantBranding()));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Tenants.FindAsync(tenant.TenantId))!.BrandingJson);
        Assert.True((await read.HandleAsync(new GetTenantBrandingQuery())).IsDefault);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("invalid JSON", null)]
    [InlineData("{\"productName\":\"Legacy\"}", "Legacy")]
    public async Task Reading_branding_preserves_legacy_fallbacks_without_changing_storage(string? json, string? name)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
        db.Tenants.Add(new TenantRow { Id = tenant.TenantId, BrandingJson = json });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new EfTenantBrandingStore(db, tenant).GetAsync();

        Assert.Equal(name, result.ProductName);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
