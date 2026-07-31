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
    private static (AuthService Service, AppDbContext Db) NewService(
        Guid? tenantId = null,
        FakeCommunicationSender? communications = null)
    {
        var tenant = new FakeCurrentTenant(tenantId ?? Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options, tenant);
        return (new AuthService(db, communications ?? new FakeCommunicationSender()), db);
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

    [Fact]
    public async Task Email_two_factor_code_is_sent_by_Postmark_and_consumed_once()
    {
        var email = new FakeCommunicationSender();
        var (auth, db) = NewService(communications: email);
        var user = await auth.CreateFirstAdminAsync(
            "owner@example.com", "Owner", "Zx7!qLmP4#vRw2");

        var challenge = await auth.IssueTwoFactorCodeAsync(user!.Id);

        Assert.Equal("ow•••@example.com", challenge.MaskedEmail);
        var sent = Assert.Single(email.AuthenticationEmails);
        Assert.Equal("owner@example.com", sent.Recipient);
        var code = System.Text.RegularExpressions.Regex.Match(sent.Body, @"\b\d{6}\b").Value;
        Assert.NotEmpty(code);
        var row = await db.Users.SingleAsync(item => item.Id == user.Id);
        Assert.NotEqual(code, row.TwoFactorCodeHash);

        Assert.True(await auth.ConfirmTwoFactorSetupAsync(user.Id, code));
        Assert.True(await auth.IsTwoFactorEnabledAsync(user.Id));
        Assert.False(await auth.VerifyTwoFactorCodeAsync(user.Id, code));
    }

    [Fact]
    public async Task Email_two_factor_invalidates_a_challenge_after_five_wrong_codes()
    {
        var email = new FakeCommunicationSender();
        var (auth, _) = NewService(communications: email);
        var user = await auth.CreateFirstAdminAsync(
            "owner@example.com", "Owner", "Zx7!qLmP4#vRw2");
        await auth.IssueTwoFactorCodeAsync(user!.Id);
        var code = System.Text.RegularExpressions.Regex.Match(
            email.AuthenticationEmails.Single().Body, @"\b\d{6}\b").Value;

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.False(await auth.ConfirmTwoFactorSetupAsync(user.Id, Wrong(code)));

        Assert.False(await auth.ConfirmTwoFactorSetupAsync(user.Id, code));
        Assert.False(await auth.IsTwoFactorEnabledAsync(user.Id));
    }

    [Fact]
    public async Task Email_two_factor_disable_requires_a_fresh_emailed_code()
    {
        var email = new FakeCommunicationSender();
        var (auth, _) = NewService(communications: email);
        var user = await auth.CreateFirstAdminAsync(
            "owner@example.com", "Owner", "Zx7!qLmP4#vRw2");
        await auth.IssueTwoFactorCodeAsync(user!.Id);
        var setupCode = Code(email.AuthenticationEmails[^1].Body);
        Assert.True(await auth.ConfirmTwoFactorSetupAsync(user.Id, setupCode));

        await auth.IssueTwoFactorCodeAsync(user.Id);
        var disableCode = Code(email.AuthenticationEmails[^1].Body);
        Assert.False(await auth.DisableTwoFactorAsync(user.Id, Wrong(disableCode)));
        Assert.True(await auth.DisableTwoFactorAsync(user.Id, disableCode));
        Assert.False(await auth.IsTwoFactorEnabledAsync(user.Id));
    }

    private static string Code(string body)
        => System.Text.RegularExpressions.Regex.Match(body, @"\b\d{6}\b").Value;
    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    private sealed class FakeCommunicationSender : IClientCommunicationSender
    {
        public string EmailProvider => "Postmark";
        public string SmsProvider => "Twilio";
        public List<(string Recipient, string Body)> AuthenticationEmails { get; } = new();

        public Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(new ClientCommsCapabilities(true, false, "Postmark", "Twilio"));

        public Task<ClientMessageDelivery> SendEmailAsync(
            string recipient, string recipientName, string subject, string body,
            CancellationToken ct = default,
            IReadOnlyList<ClientEmailAttachment>? attachments = null)
            => Task.FromResult(new ClientMessageDelivery("Postmark", "email-id"));

        public Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
            string recipient, string recipientName, string subject, string body,
            CancellationToken ct = default)
        {
            AuthenticationEmails.Add((recipient, body));
            return Task.FromResult(new ClientMessageDelivery("Postmark", "auth-id"));
        }

        public Task<ClientMessageDelivery> SendSmsAsync(
            string recipient, string body, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
