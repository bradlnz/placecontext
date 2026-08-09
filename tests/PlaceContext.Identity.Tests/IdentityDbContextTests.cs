using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Infrastructure.Persistence;

namespace PlaceContext.Identity.Tests;

public sealed class IdentityDbContextTests
{
    [Fact]
    public void Model_contains_only_identity_owned_tables()
    {
        using var db = CreateContext(Guid.NewGuid());

        var tables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "DataProtectionKeys", "tenants", "users", "invites", "role_definitions",
                "user_permission_grants", "user_api_tokens", "oauth_clients",
                "oauth_refresh_tokens", "oauth_auth_codes"
            },
            tables!);
    }

    [Fact]
    public async Task SaveChanges_stamps_current_tenant_on_new_owned_rows()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var user = new UserRow { Id = Guid.NewGuid(), Email = "owner@example.com" };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Equal(tenantId, user.TenantId);
    }

    private static IdentityDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options, new TestTenant(tenantId));
    }

    private sealed record TestTenant(Guid TenantId) : ICurrentTenant
    {
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }
}