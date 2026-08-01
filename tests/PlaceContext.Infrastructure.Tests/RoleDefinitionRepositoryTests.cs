using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Role definitions against a real (EF Core In-Memory) <see cref="AppDbContext"/>: the lazy per-tenant
/// system-role seed, CRUD, and <see cref="PermissionService"/> resolving effective permissions from the
/// DB grant set (with the hardcoded defaults as fallback) under the default-admin gate.
/// </summary>
public class RoleDefinitionRepositoryTests
{
    private static (ServiceProvider Provider, FakeCurrentTenant Tenant) NewServices()
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(tenant);
        // The database name must be hoisted out of the options lambda — it runs per context instance,
        // so an inline Guid would give every scope its own isolated InMemory database.
        var dbName = Guid.NewGuid().ToString("N");
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IUserPermissionGrantRepository, EfUserPermissionGrantRepository>();
        services.AddScoped<IRoleDefinitionRepository, EfRoleDefinitionRepository>();
        return (services.BuildServiceProvider(), tenant);
    }

    private static UserRow AddUser(AppDbContext db, string role, bool isDefaultAdmin = false)
    {
        var row = new UserRow
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Member",
            PasswordHash = "x",
            PasswordSet = true,
            Role = role,
            IsDefaultAdmin = isDefaultAdmin,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(row);
        return row;
    }

    // ── Lazy system-role seed ────────────────────────────────────────────────────

    [Fact]
    public async Task The_first_read_seeds_the_four_system_roles_from_the_hardcoded_defaults()
    {
        var (provider, _) = NewServices();
        using var scope = provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();

        var roles = await repo.ListAsync();

        Assert.Equal(4, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsSystem));
        foreach (var role in Enum.GetValues<UserRole>())
        {
            var seeded = roles.Single(r => r.Name == role.ToString());
            Assert.Equal(
                RolePermissionDefaults.GetDefaults(role).OrderBy(p => p, StringComparer.Ordinal),
                seeded.Permissions);
        }
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_update_and_delete_a_custom_role_round_trips()
    {
        var (provider, _) = NewServices();
        using var scope = provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = await repo.CreateAsync("Support", new[] { Permission.JobsView, Permission.DataRead });
        await db.SaveChangesAsync();

        var loaded = await repo.GetByNameAsync("Support");
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsSystem);
        Assert.Equal(new[] { Permission.DataRead, Permission.JobsView }, loaded.Permissions); // sorted

        await repo.SetPermissionsAsync(created.Id, new[] { Permission.JobsView });
        await db.SaveChangesAsync();
        Assert.Equal(new[] { Permission.JobsView }, (await repo.GetByIdAsync(created.Id))!.Permissions);

        await repo.DeleteAsync(created.Id);
        await db.SaveChangesAsync();
        Assert.Null(await repo.GetByIdAsync(created.Id));
    }

    // ── PermissionService: DB grant set + default-admin gate ─────────────────────

    [Fact]
    public async Task Effective_permissions_come_from_the_db_role_grant_set()
    {
        var (provider, _) = NewServices();
        Guid userId;
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = setup.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();
            userId = AddUser(db, "Admin").Id;
            // Edit the Admin role in the DB: drop jobs.edit from the full catalog.
            var admin = (await repo.GetByNameAsync("Admin"))!;
            await repo.SetPermissionsAsync(admin.Id,
                admin.Permissions.Where(p => p != Permission.JobsEdit).ToList());
            await db.SaveChangesAsync();
        }
        var svc = new PermissionService(new FakeCurrentUser(), provider.GetRequiredService<IServiceScopeFactory>());

        var effective = await svc.GetEffectivePermissionsForUserAsync(userId, "Admin");

        Assert.DoesNotContain(Permission.JobsEdit, effective); // DB grant set, not the hardcoded full catalog
        Assert.Contains(Permission.JobsView, effective);
    }

    [Fact]
    public async Task Effective_permissions_fall_back_to_the_hardcoded_defaults_when_no_role_row_exists()
    {
        var (provider, _) = NewServices();
        Guid userId;
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = setup.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();
            userId = AddUser(db, "Admin").Id;
            // Force the seed, then delete the Admin row — resolution must fall back to the hardcoded map.
            var admin = (await repo.GetByNameAsync("Admin"))!;
            await repo.DeleteAsync(admin.Id);
            await db.SaveChangesAsync();
        }
        var svc = new PermissionService(new FakeCurrentUser(), provider.GetRequiredService<IServiceScopeFactory>());

        var effective = await svc.GetEffectivePermissionsForUserAsync(userId, "Admin");

        Assert.Contains(Permission.JobsEdit, effective);
        Assert.Contains(Permission.MembersManage, effective);
    }

    [Fact]
    public async Task Settings_manage_is_never_effective_for_a_non_default_admin()
    {
        var (provider, _) = NewServices();
        Guid userId;
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = AddUser(db, "Admin").Id; // Admin grants settings.manage — the gate strips it anyway
            await db.SaveChangesAsync();
        }
        var svc = new PermissionService(new FakeCurrentUser(), provider.GetRequiredService<IServiceScopeFactory>());

        var effective = await svc.GetEffectivePermissionsForUserAsync(userId, "Admin");

        Assert.DoesNotContain(Permission.SettingsManage, effective);
        Assert.Contains(Permission.MembersManage, effective);
    }

    [Fact]
    public async Task Settings_manage_is_effective_for_the_default_admin()
    {
        var (provider, _) = NewServices();
        Guid userId;
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = AddUser(db, "Owner", isDefaultAdmin: true).Id;
            await db.SaveChangesAsync();
        }
        var svc = new PermissionService(new FakeCurrentUser(), provider.GetRequiredService<IServiceScopeFactory>());

        var effective = await svc.GetEffectivePermissionsForUserAsync(userId, "Owner");

        Assert.Contains(Permission.SettingsManage, effective);
    }

    [Fact]
    public async Task Assigning_a_custom_role_persists_and_resolves_that_roles_grant_set()
    {
        var (provider, _) = NewServices();
        Guid userId;
        using (var setup = provider.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = setup.ServiceProvider.GetRequiredService<IRoleDefinitionRepository>();
            userId = AddUser(db, "Member").Id;
            await repo.CreateAsync("Support", new[] { Permission.JobsView, Permission.DataRead });
            await db.SaveChangesAsync();

            var members = new MembershipService(db, new FakeCurrentUser(), repo);
            await members.SetRoleAsync(userId, "Support");
        }

        // The assignment persisted by name…
        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Support", (await db.Users.SingleAsync(u => u.Id == userId)).Role);
        }
        // …and permission resolution uses the custom role's grant set, not any enum default.
        var svc = new PermissionService(new FakeCurrentUser(), provider.GetRequiredService<IServiceScopeFactory>());
        var effective = await svc.GetEffectivePermissionsForUserAsync(userId, "Support");

        Assert.Contains(Permission.JobsView, effective);
        Assert.Contains(Permission.DataRead, effective);
        Assert.DoesNotContain(Permission.JobsEdit, effective); // a plain Member default would grant this
    }

    /// <summary>Unused by these tests (resolution is for an explicit user), required by the ctor.</summary>
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.Empty;
        public string Role => "Viewer";
        public bool IsAuthenticated => false;
    }
}
