using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;

namespace PlaceContext.Infrastructure.Tests.Auth;

public sealed class ManagedWorkspaceSignupPostgresTests
{
    [AgentPostgresFact]
    public async Task Email_challenge_is_required_and_confirming_creates_only_one_tenant_owner_project()
    {
        var schema = "signup_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            CurrentTenant.Clear();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs)
                .Options;
            await using var db = new AppDbContext(options, new CurrentTenant());
            await db.Database.MigrateAsync();
            var mailer = new FakeMailer();
            var service = NewService(db, mailer, Environments.Production);
            var input = ValidInput();
            var started = await service.StartAsync(input, new Uri("https://placecontext.io"));
            Assert.True(started.RequiresVerification);
            Assert.Null(started.ExchangeCode);
            Assert.Equal(0, await db.Tenants.CountAsync());
            Assert.Equal(0, await db.Users.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.Projects.IgnoreQueryFilters().CountAsync());

            var pending = await db.PendingWorkspaceSignups.SingleAsync();
            Assert.NotEqual(input.Password, pending.PasswordHash);
            Assert.DoesNotContain(input.Password, pending.PasswordHash);
            Assert.Equal("standard", pending.Plan);
            var verificationToken = mailer.Link!.Query.Split("verify=")[1];
            Assert.DoesNotContain(verificationToken, pending.TokenHash);
            Assert.Equal(input.Email, mailer.Email);

            var activation = await service.VerifyAsync(verificationToken);
            Assert.Equal("acme-ops", activation.Slug);
            Assert.NotEqual(verificationToken, activation.ExchangeCode);
            Assert.Null(CurrentTenant.Current);
            var tenant = await db.Tenants.SingleAsync();
            Assert.Equal(input.WorkspaceName, tenant.Name);
            Assert.Equal("acme-ops", tenant.Slug);
            var owner = await db.Users.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(tenant.Id, owner.TenantId);
            Assert.True(owner.PasswordSet);
            Assert.True(owner.IsDefaultAdmin);
            Assert.Equal("Owner", owner.Role);
            Assert.Equal(input.Email, owner.Email);
            var project = await db.Projects.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(tenant.Id, project.TenantId);
            Assert.Equal("Default project", project.Name);
            Assert.Equal("Registered", project.Status);
            var roles = await db.RoleDefinitions.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(Enum.GetValues<UserRole>().Length, roles.Count);
            Assert.All(roles, role => Assert.True(role.IsSystem));
            Assert.Contains(roles, role => role.Name == nameof(UserRole.Owner));
            Assert.Equal(0, await db.TenantRunPlans.IgnoreQueryFilters().CountAsync()); // Signup never grants a paid plan.

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(verificationToken));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.RedeemAsync(activation.ExchangeCode, Guid.NewGuid()));
            CurrentTenant.Set(new TenantInfo(tenant.Id, tenant.Slug, tenant.Name, "UTC"));
            try
            {
                var signedIn = await service.RedeemAsync(activation.ExchangeCode, tenant.Id);
                Assert.Equal(tenant.Id, signedIn.TenantId);
                Assert.Equal(owner.Id, signedIn.UserId);
                Assert.Equal("standard", signedIn.Plan);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    service.RedeemAsync(activation.ExchangeCode, tenant.Id));
            }
            finally
            {
                CurrentTenant.Clear();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StartAsync(input, new Uri("https://placecontext.io")));
            Assert.Equal(1, await db.Tenants.CountAsync());
        }
        finally
        {
            CurrentTenant.Clear();
            await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Rejects_untrusted_slugs_passwords_and_free_managed_plan()
    {
        foreach (var invalid in new[] { "api", "default", "www", "A-Company", "a", "a--b",
                     "a.b", "something.", "-abcd", "signup" })
        {
            Assert.NotNull(WorkspaceSignupValidation.ValidateSlug(invalid));
        }
        Assert.Null(WorkspaceSignupValidation.ValidateSlug("acme-ops"));
        Assert.NotNull(WorkspaceSignupValidation.ValidateSignup(ValidInput() with
        {
            Plan = "free"
        }));
        Assert.NotNull(WorkspaceSignupValidation.ValidateSignup(ValidInput() with
        {
            Password = "abc",
            ConfirmPassword = "abc"
        }));
        Assert.NotNull(WorkspaceSignupValidation.ValidateSignup(ValidInput() with
        {
            Email = "not-an-email"
        }));
    }

    [AgentPostgresFact]
    public async Task Production_never_skips_email_verification_and_disabled_signup_cannot_create_a_tenant()
    {
        var schema = "signup_lock_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            CurrentTenant.Clear();
            await using var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs)
                .Options, new CurrentTenant());
            await db.Database.MigrateAsync();
            var noMail = new FakeMailer { IsConfigured = false };
            var prod = NewService(db, noMail, Environments.Production,
                skipDevelopmentVerification: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                prod.StartAsync(ValidInput(), new Uri("https://placecontext.io")));
            var disabled = NewService(db, new FakeMailer(), Environments.Development,
                signupEnabled: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                disabled.StartAsync(ValidInput(), new Uri("http://localhost:7700")));
            Assert.Empty(await db.Tenants.ToListAsync());
            Assert.Empty(await db.PendingWorkspaceSignups.ToListAsync());
        }
        finally
        {
            CurrentTenant.Clear();
            await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static WorkspaceSignupInput ValidInput() => new(
        "Acme Operations", "acme-ops", "owner@example.test",
        "Acme Owner", "Zx7!qLmP4#vRw2", "Zx7!qLmP4#vRw2", "standard");

    private static ManagedWorkspaceSignup NewService(AppDbContext db, FakeMailer mailer,
        string environment, bool signupEnabled = true, bool skipDevelopmentVerification = false) =>
        new(db, Options.Create(new RunBillingDeploymentOptions
        {
            ManagedHosting = true,
            RequireExplicitTenantProvisioning = true
        }), Options.Create(new WorkspaceSignupOptions
        {
            Enabled = signupEnabled,
            AllowUnverifiedDevelopmentSignup = skipDevelopmentVerification
        }), new FakeHostEnvironment { EnvironmentName = environment }, mailer);

    private sealed class FakeMailer : IWorkspaceSignupEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public Uri? Link;
        public string? Email;
        public Task SendVerificationAsync(string email, string displayName,
            Uri confirmationLink, CancellationToken ct = default)
        {
            Email = email;
            Link = confirmationLink;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "PlaceContext.Tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
