using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Tenancy;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

/// <summary>Public, rate-limited registration with CSRF protection and email verification.
/// In production only a verified email can create a tenant, and only a one-use
/// workspace-host exchange ticket can establish an authentication cookie.</summary>
[AllowAnonymous]
public sealed class ManagedWorkspaceSignupController(
    ManagedWorkspaceSignup registration, IConfiguration configuration,
    IOptions<RunBillingDeploymentOptions> hosting,
    IAntiforgery antiforgery, ITenantStore tenants,
    ILogger<ManagedWorkspaceSignupController> logger) : ControllerBase
{
    [HttpPost("/auth/signup")]
    [EnableRateLimiting("signup")]
    [RequestSizeLimit(32 * 1024)]
    public async Task<IActionResult> Signup(
        [FromForm] string workspaceName,
        [FromForm] string workspace,
        [FromForm] string email,
        [FromForm] string? displayName,
        [FromForm] string password,
        [FromForm] string confirmPassword,
        [FromForm] string plan,
        CancellationToken ct)
    {
        if (!ApexRegistrationAllowed()) return NotFound();
        if (!await IsValidCsrfAsync())
            return SignupError("Your session expired. Reload the page and try again.", plan, workspace);
        var input = new WorkspaceSignupInput(workspaceName ?? "", workspace ?? "", email ?? "",
            displayName ?? "", password ?? "", confirmPassword ?? "", plan ?? "");
        try
        {
            var result = await registration.StartAsync(input, ManagedSignupUrls.PublicOrigin(configuration)!, ct);
            if (result.RequiresVerification) return Redirect("/signup?sent=1");
            return Redirect(ManagedSignupUrls.TenantUri(configuration, result.Slug!,
                "/auth/signup/complete").AbsoluteUri +
                "?code=" + Uri.EscapeDataString(result.ExchangeCode!));
        }
        catch (ArgumentException ex)
        {
            return SignupError(ex.Message, plan, workspace);
        }
        catch (InvalidOperationException ex) when (ex.Message == "That workspace address is already in use.")
        {
            return SignupError(ex.Message, plan, workspace);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace signup could not finish.");
            return SignupError("Registration is temporarily unavailable. Try again later.", plan, workspace);
        }
    }

    [HttpPost("/auth/signup/verify")]
    [EnableRateLimiting("signup")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Verify([FromForm] string token, CancellationToken ct)
    {
        if (!ApexRegistrationAllowed()) return NotFound();
        if (!await IsValidCsrfAsync())
            return SignupError("Your session expired. Open the verification link again.");
        try
        {
            var result = await registration.VerifyAsync(token, ct);
            return Redirect(ManagedSignupUrls.TenantUri(configuration, result.Slug,
                "/auth/signup/complete").AbsoluteUri +
                "?code=" + Uri.EscapeDataString(result.ExchangeCode));
        }
        catch (UnauthorizedAccessException)
        {
            return SignupError("Your verification link is invalid or expired.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "That workspace address is already in use.")
        {
            return SignupError(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace verification failed.");
            return SignupError("Your verification link is invalid or expired.");
        }
    }

    [HttpGet("/auth/signup/complete")]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> Complete([FromQuery] string? code, CancellationToken ct)
    {
        if (!registration.Enabled || CurrentTenant.Current is not { Slug: not "default" } tenant
            || code is null) return NotFound();
        if (!string.Equals(Request.Host.Host, ManagedSignupUrls.TenantUri(configuration, tenant.Slug).Host,
                StringComparison.OrdinalIgnoreCase))
            return NotFound();
        try
        {
            var identity = await registration.RedeemAsync(code, tenant.Id, ct);
            await AuthController.SignInAsync(HttpContext,
                new AuthUser(identity.UserId, identity.TenantId,
                    identity.Email, identity.DisplayName, "Owner"));
            return Redirect("/settings/billing?welcome=1&start=" +
                Uri.EscapeDataString(identity.Plan));
        }
        catch (UnauthorizedAccessException) { return Redirect("/login?error=" +
            Uri.EscapeDataString("That sign-in link expired. Sign in with the password you created.")); }
    }

    [HttpPost("/auth/workspace")]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> FindWorkspace([FromForm] string? workspace, CancellationToken ct)
    {
        if (!hosting.Value.ManagedHosting
            || !ManagedSignupUrls.IsApexHost(Request.Host.Host, configuration))
            return NotFound();
        if (!await IsValidCsrfAsync())
            return Redirect("/login?error=" + Uri.EscapeDataString("Reload and try again."));
        var slug = (workspace ?? "").Trim().ToLowerInvariant();
        if (ManagedWorkspaceSignup.ValidateSlug(slug) is not null
            || await tenants.FindBySlugAsync(slug, ct) is null)
            return Redirect("/login?error=" + Uri.EscapeDataString(
                "We couldn't find that workspace. Check its address or create one."));
        return Redirect(ManagedSignupUrls.TenantUri(configuration, slug, "/login").AbsoluteUri);
    }

    private bool ApexRegistrationAllowed() => registration.Enabled
        && ManagedSignupUrls.IsApexHost(Request.Host.Host, configuration);

    private async Task<bool> IsValidCsrfAsync()
    {
        try { await antiforgery.ValidateRequestAsync(HttpContext); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private IActionResult SignupError(string error, string? plan = null, string? slug = null)
    {
        var query = "?error=" + Uri.EscapeDataString(error);
        if (plan is RunPlans.Basic or RunPlans.Standard)
            query += "&plan=" + Uri.EscapeDataString(plan);
        if (!string.IsNullOrWhiteSpace(slug) && slug.Length < 64)
            query += "&workspace=" + Uri.EscapeDataString(slug);
        return Redirect("/signup" + query);
    }
}
