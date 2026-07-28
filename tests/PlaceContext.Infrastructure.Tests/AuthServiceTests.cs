using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// First-run admin setup against a real (EF Core In-Memory) <see cref="AppDbContext"/> — the tenant
/// query filter, the PBKDF2 hashing, and the "unconfigured" detection all run for real; only the
/// database engine underneath is swapped for the In-Memory provider so no Postgres is required.
/// </summary>
public class AuthServiceTests
{
    private static (AuthService Service, AppDbContext Db) NewService(Guid? tenantId = null)
    {
        var tenant = new FakeCurrentTenant(tenantId ?? Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options, tenant);
        return (new AuthService(db, tenant, new FakeDataEncryptor()), db);
    }

    // ── First-run detection ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_brand_new_tenant_is_unconfigured()
    {
        var (auth, _) = NewService();

        Assert.True(await auth.IsUnconfiguredAsync());
    }

    [Fact]
    public async Task The_machine_provisioned_operator_row_does_not_count_as_configured()
    {
        // GetOrCreateOperatorAsync backs the HMAC portal-token path (pctl TUI / headless) and must keep
        // working unchanged — but its unusable, random password must never satisfy first-run detection.
        var (auth, _) = NewService();

        await auth.GetOrCreateOperatorAsync();

        Assert.True(await auth.IsUnconfiguredAsync());
    }

    [Fact]
    public async Task Unconfigured_flips_to_configured_once_the_first_admin_is_created()
    {
        var (auth, _) = NewService();

        var admin = await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        Assert.NotNull(admin);
        Assert.False(await auth.IsUnconfiguredAsync());
    }

    // ── First-admin creation ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFirstAdmin_sets_an_Owner_with_a_verifiable_PBKDF2_hash()
    {
        var (auth, db) = NewService();

        var admin = await auth.CreateFirstAdminAsync("owner@example.com", "Owner Person", "Zx7!qLmP4#vRw2");

        Assert.NotNull(admin);
        Assert.Equal(UserRole.Owner, admin!.Role);
        Assert.Equal("owner@example.com", admin.Email);

        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == admin.Id);
        Assert.True(row.PasswordSet);
        Assert.Equal(UserRole.Owner.ToString(), row.Role);
        // A real, verifiable PBKDF2 hash — not the placeholder used by the machine-operator row — and
        // the wrong password must not verify against it.
        Assert.True(PasswordHasher.Verify("Zx7!qLmP4#vRw2", row.PasswordHash));
        Assert.False(PasswordHasher.Verify("something-else-entirely", row.PasswordHash));
    }

    [Fact]
    public async Task CreateFirstAdmin_defaults_the_display_name_from_the_email_when_blank()
    {
        var (auth, _) = NewService();

        var admin = await auth.CreateFirstAdminAsync("owner@example.com", "  ", "Zx7!qLmP4#vRw2");

        Assert.Equal("owner", admin!.DisplayName);
    }

    [Fact]
    public async Task CreateFirstAdmin_refuses_once_the_tenant_is_already_configured()
    {
        var (auth, _) = NewService();
        var first = await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");
        Assert.NotNull(first);

        // A second attempt — whether a retry, a race, or someone probing the endpoint after setup —
        // must fail closed rather than mint a second admin.
        var second = await auth.CreateFirstAdminAsync("someone-else@example.com", "Someone Else", "An0ther$trongPass1");

        Assert.Null(second);
    }

    [Fact]
    public async Task CreateFirstAdmin_refuses_a_duplicate_email_within_the_tenant()
    {
        var (auth, db) = NewService();

        // Seed a non-Owner member directly so the tenant is still "unconfigured" by our definition, but
        // the email is already taken.
        db.Users.Add(new UserRow
        {
            Id = Guid.NewGuid(),
            Email = "owner@example.com",
            DisplayName = "Existing",
            PasswordHash = "irrelevant",
            Role = UserRole.Member.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var admin = await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        Assert.Null(admin);
    }

    // ── Credential validation (password login) ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateCredentials_succeeds_for_the_right_email_and_password()
    {
        var (auth, _) = NewService();
        await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        var user = await auth.ValidateCredentialsAsync("owner@example.com", "Zx7!qLmP4#vRw2");

        Assert.NotNull(user);
        Assert.Equal(UserRole.Owner, user!.Role);
    }

    [Fact]
    public async Task ValidateCredentials_fails_for_the_wrong_password()
    {
        var (auth, _) = NewService();
        await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        var user = await auth.ValidateCredentialsAsync("owner@example.com", "wrong-password-entirely");

        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateCredentials_fails_for_an_unknown_email()
    {
        var (auth, _) = NewService();
        await auth.CreateFirstAdminAsync("owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        var user = await auth.ValidateCredentialsAsync("nobody@example.com", "Zx7!qLmP4#vRw2");

        Assert.Null(user);
    }

    [Fact]
    public async Task ValidateCredentials_fails_for_the_machine_operator_row_which_has_no_real_password()
    {
        var (auth, _) = NewService();
        var operatorUser = await auth.GetOrCreateOperatorAsync();

        // Nobody holds this row's random placeholder password — password login must never succeed for it.
        var user = await auth.ValidateCredentialsAsync(operatorUser.Email, "any-guess-at-all-here");

        Assert.Null(user);
    }
}
