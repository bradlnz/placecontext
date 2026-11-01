using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PlaceContext.Application.Access;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Billing;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>
/// Email-verified, explicit SaaS tenant registration. Anonymous users can only reserve
/// short-lived pending records. A verified email creates the tenant, owner and default
/// project in one PostgreSQL transaction; a second one-use ticket signs the owner in on
/// the tenant's own hostname (no cross-subdomain authentication cookie).
/// </summary>
public sealed class ManagedWorkspaceSignup(
    AppDbContext db, IOptions<RunBillingDeploymentOptions> deployment,
    IOptions<WorkspaceSignupOptions> settings, IHostEnvironment environment,
    IWorkspaceSignupEmailSender mailer)
{
    private static readonly Regex SlugRule = new(
        "^[a-z][a-z0-9-]{2,38}[a-z0-9]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "www", "api", "app", "auth", "admin", "billing", "login",
        "signup", "register", "setup", "support", "help", "docs", "status",
        "mcp", "static", "assets", "localhost", "system", "internal",
        "security", "mail", "smtp", "root", "demo"
    };

    public bool Enabled => deployment.Value.ManagedHosting
        && deployment.Value.RequireExplicitTenantProvisioning && settings.Value.Enabled;

    private bool LocalUnverified => environment.IsDevelopment()
        && settings.Value.AllowUnverifiedDevelopmentSignup;

    public static string? ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)
            || !SlugRule.IsMatch(slug)
            || Reserved.Contains(slug)
            || slug.Contains("--", StringComparison.Ordinal))
            return "Use 4–40 lowercase letters, numbers or hyphens; start with a letter and end with a letter or number.";
        return null;
    }

    public static string? ValidateSignup(WorkspaceSignupInput input)
    {
        if (string.IsNullOrWhiteSpace(input.WorkspaceName)
            || input.WorkspaceName.Trim().Length is < 2 or > 90)
            return "Enter a workspace name between 2 and 90 characters.";
        var slugError = ValidateSlug(input.Slug);
        if (slugError is not null) return slugError;
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254
            || !MailAddress.TryCreate(input.Email, out var parsed)
            || !string.Equals(parsed.Address, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            return "Enter a valid work email.";
        if (!string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Trim().Length > 120)
            return "Your name must be shorter than 120 characters.";
        if (input.Plan is not (RunPlans.Basic or RunPlans.Standard))
            return "Choose Basic or Standard Managed.";
        return PasswordPolicy.Validate(input.Password, input.ConfirmPassword);
    }

    public async Task<WorkspaceSignupStart> StartAsync(
        WorkspaceSignupInput input, Uri publicOrigin, CancellationToken ct = default)
    {
        RequireEnabled();
        ValidateOrigin(publicOrigin);
        input = input with { Slug = input.Slug.Trim().ToLowerInvariant(),
            Email = input.Email.Trim().ToLowerInvariant(),
            WorkspaceName = input.WorkspaceName.Trim(),
            DisplayName = input.DisplayName.Trim() };
        if (ValidateSignup(input) is { } error) throw new ArgumentException(error);
        if (!LocalUnverified && !mailer.IsConfigured)
            throw new InvalidOperationException("Workspace verification email is not configured.");
        if (await db.Tenants.AnyAsync(t => t.Slug == input.Slug, ct))
            throw new InvalidOperationException("That workspace address is already in use.");

        var token = GenerateToken();
        db.PendingWorkspaceSignups.Add(new PendingWorkspaceSignupRow
        {
            Id = Guid.NewGuid(),
            Slug = input.Slug,
            WorkspaceName = input.WorkspaceName,
            Email = input.Email,
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName)
                ? input.Email.Split('@')[0] : input.DisplayName,
            PasswordHash = PasswordHasher.Hash(input.Password),
            Plan = input.Plan,
            TokenHash = TokenHash(token),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync(ct);

        if (LocalUnverified)
        {
            // Never enabled outside Development. An isolated Playwright sandbox can
            // traverse the registration flow without sending third-party mail.
            var activation = await VerifyAsync(token, ct);
            return new WorkspaceSignupStart(false, activation.ExchangeCode, activation.Slug, input.Plan);
        }

        var link = new Uri(publicOrigin,
            "/signup?verify=" + Uri.EscapeDataString(token));
        await mailer.SendVerificationAsync(input.Email,
            string.IsNullOrWhiteSpace(input.DisplayName) ? input.Email.Split('@')[0] : input.DisplayName,
            link, ct);
        return new WorkspaceSignupStart(true);
    }

    public async Task<WorkspaceSignupActivation> VerifyAsync(string token, CancellationToken ct = default)
    {
        RequireEnabled();
        var tokenHash = TokenHash(token);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pending = await db.PendingWorkspaceSignups.FromSqlInterpolated($"""
            SELECT * FROM pending_workspace_signups
            WHERE "TokenHash" = {tokenHash} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (pending is null || pending.ExpiresAt <= DateTimeOffset.UtcNow
            || pending.VerifiedAt is not null)
            throw new InvalidOperationException("This verification link is invalid or has expired.");

        if (await db.Tenants.AnyAsync(t => t.Slug == pending.Slug, ct))
            throw new InvalidOperationException("That workspace address is already in use.");

        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var code = GenerateToken();
        var prior = CurrentTenant.Current;
        CurrentTenant.Set(new TenantInfo(tenantId, pending.Slug, pending.WorkspaceName, "UTC"));
        try
        {
            db.Tenants.Add(new TenantRow
            {
                Id = tenantId, Slug = pending.Slug, Name = pending.WorkspaceName,
                TimeZoneId = "UTC", CreatedAt = now
            });
            db.Users.Add(new UserRow
            {
                Id = userId, TenantId = tenantId, Email = pending.Email,
                DisplayName = pending.DisplayName, PasswordHash = pending.PasswordHash,
                PasswordSet = true, Role = "Owner", IsDefaultAdmin = true,
                CreatedAt = now
            });
            db.Projects.Add(new ProjectRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = "Default project",
                Path = "/workspace/default", Status = "Registered",
                DiscoveredAt = now, RegisteredAt = now
            });
            // Materialize the built-in access roles atomically with the owner.
            // A fresh portal opens several authenticated components in parallel;
            // without these rows all of them race the lazy role seeder, producing
            // expected-but-noisy PostgreSQL unique-key errors on first sign-in.
            foreach (var role in Enum.GetValues<UserRole>())
                db.RoleDefinitions.Add(new RoleDefinitionRow
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = role.ToString(),
                    IsSystem = true,
                    PermissionsJson = JsonSerializer.Serialize(
                        RolePermissionDefaults.GetDefaults(role)
                            .OrderBy(permission => permission, StringComparer.Ordinal))
                });
            pending.VerifiedAt = now;
            pending.TenantId = tenantId;
            pending.UserId = userId;
            pending.ExchangeHash = TokenHash(code);
            pending.ExchangeExpiresAt = now.AddMinutes(5);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new InvalidOperationException("That workspace address is already in use.", ex);
        }
        finally
        {
            if (prior is null) CurrentTenant.Clear();
            else CurrentTenant.Set(prior);
        }
        return new WorkspaceSignupActivation(pending.Slug, code);
    }

    public async Task<WorkspaceSignupIdentity> RedeemAsync(
        string code, Guid tenantId, CancellationToken ct = default)
    {
        RequireEnabled();
        if (tenantId == Guid.Empty) throw new UnauthorizedAccessException("Workspace is required.");
        var hash = TokenHash(code);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pending = await db.PendingWorkspaceSignups.FromSqlInterpolated($"""
            SELECT * FROM pending_workspace_signups
            WHERE "ExchangeHash" = {hash} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (pending is null || pending.TenantId != tenantId
            || pending.UserId is null || pending.VerifiedAt is null
            || pending.ExchangeRedeemedAt is not null
            || pending.ExchangeExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("This sign-in link is invalid or has expired.");

        // A ticket is never sufficient to select another workspace. The host's
        // resolved tenant must agree with the verified tenant in this row.
        var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(u =>
            u.Id == pending.UserId && u.TenantId == tenantId, ct);
        if (owner is null || owner.Role != "Owner" || !owner.PasswordSet)
            throw new UnauthorizedAccessException("The workspace owner is no longer available.");
        pending.ExchangeRedeemedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new WorkspaceSignupIdentity(tenantId, owner.Id,
            owner.Email, owner.DisplayName, pending.Plan);
    }

    public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string TokenHash(string token)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("The verification link is invalid.");
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token.ToUpperInvariant())));
    }

    private void RequireEnabled()
    {
        if (!Enabled) throw new InvalidOperationException("Managed workspace registration is not available.");
    }

    private void ValidateOrigin(Uri origin)
    {
        if (!origin.IsAbsoluteUri
            || origin.Scheme != Uri.UriSchemeHttps
                && !(environment.IsDevelopment() && origin.IsLoopback
                    && origin.Scheme == Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new InvalidOperationException("A trusted HTTPS signup origin is required.");
    }
}
