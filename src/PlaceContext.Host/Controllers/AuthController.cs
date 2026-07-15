using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Sign-in, sign-out, first-run setup, and invite acceptance.
///
/// Password login (added here): a fresh tenant is "unconfigured" until an operator completes /setup
/// (email + strong password → the tenant's Owner); from then on /login verifies email + password. In
/// Development, /locked keeps auto-signing the operator in with no password (unchanged — the team's
/// local workflow and the Playwright/verify harness rely on it); everywhere else /locked now redirects
/// to /setup or /login instead of signing anyone in. The pctl TUI's HMAC-token machine path
/// (/auth/portal) is untouched — it never counts towards "configured" (see IAuthService.GetOrCreateOperatorAsync),
/// so it can keep bootstrapping a headless session without ever needing a password. /join turns an
/// invite token into a member. All actions here are anonymous by design — they run before a session
/// cookie exists.
/// </summary>
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly PortalToken _portal;
    private readonly IMembershipService _members;
    private readonly IAntiforgery _antiforgery;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService auth, PortalToken portal, IMembershipService members,
        IAntiforgery antiforgery, IConfiguration config, IWebHostEnvironment env)
    {
        _auth = auth;
        _portal = portal;
        _members = members;
        _antiforgery = antiforgery;
        _config = config;
        _env = env;
    }

    // Token sign-in (self-hosted; the pctl TUI mints the token and opens /auth/portal).
    // The portal has no password login. A valid short-lived token (HMAC-signed with the shared
    // PlaceContext:Portal:SigningKey) signs the cluster operator into the cookie. In Development with no
    // key configured, sign-in is automatic so `./run.sh` + opening localhost just works with no cluster.
    [HttpGet("/auth/portal")]
    public async Task<IActionResult> Portal(string? token, string? returnUrl)
    {
        var portalSigningKey = _config["PlaceContext:Portal:SigningKey"];
        var devAutoLogin = _env.IsDevelopment() && string.IsNullOrWhiteSpace(portalSigningKey);

        if (!devAutoLogin && !_portal.TryValidate(token, portalSigningKey, DateTimeOffset.UtcNow))
            return Redirect("/locked");
        var operatorUser = await _auth.GetOrCreateOperatorAsync(HttpContext.RequestAborted);
        await SignInAsync(HttpContext, operatorUser);
        return Redirect(LocalOrHome(returnUrl));
    }

    // The cookie scheme's LoginPath: every unauthenticated request to a protected page lands here.
    //   • Development: unchanged — auto-signs in as the operator with no password, exactly as before, so
    //     local dev and the Playwright/verify harness keep working with zero setup.
    //   • Everywhere else: no more password-less auto-login. Send the operator to /setup (tenant has no
    //     admin with a real password yet) or /login (it does), carrying the return URL through either way.
    [HttpGet("/locked")]
    public async Task<IActionResult> Locked()
    {
        if (_env.IsDevelopment())
        {
            var operatorUser = await _auth.GetOrCreateOperatorAsync(HttpContext.RequestAborted);
            await SignInAsync(HttpContext, operatorUser);
            // Honour the cookie middleware's ReturnUrl so deep links survive the auto-login round trip.
            return Redirect(LocalOrHome(Request.Query["ReturnUrl"]));
        }

        var returnUrl = LocalOrHome(Request.Query["ReturnUrl"]);
        var target = await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted) ? "/setup" : "/login";
        return Redirect(returnUrl == "/" ? target : $"{target}?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    // First-run setup: creates this tenant's Owner (email + strong password) and signs them straight in.
    // Fails closed — refuses (redirects to /login) once an admin already exists, so this can never be
    // replayed into a second admin. Rendered by the Setup.razor page; posted as a plain HTML form (not a
    // Blazor circuit) so the response can set the auth cookie directly, same as every other sign-in path
    // on this controller.
    [HttpPost("/auth/setup")]
    public async Task<IActionResult> Setup([FromForm] string email, [FromForm] string? displayName,
        [FromForm] string password, [FromForm] string confirmPassword)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect("/setup?error=" + Uri.EscapeDataString("Your session expired — please try again."));

        if (!await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted))
            return Redirect("/login"); // someone already finished setup — this page is a dead end now

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Redirect("/setup?error=" + Uri.EscapeDataString("Enter a valid email address."));

        // Defense in depth: the Setup page enforces this client-side too, but the server never trusts that.
        var policyError = PasswordPolicy.Validate(password, confirmPassword);
        if (policyError is not null)
            return Redirect("/setup?error=" + Uri.EscapeDataString(policyError));

        var admin = await _auth.CreateFirstAdminAsync(email, displayName ?? "", password, HttpContext.RequestAborted);
        if (admin is null)
            return Redirect("/login"); // lost a race to a concurrent /setup submission

        await SignInAsync(HttpContext, admin);
        return Redirect("/");
    }

    /// <summary>
    /// Whether first-run admin setup is still needed. Used by the install CLI/TUI to decide whether
    /// to prompt for the default admin account.
    /// </summary>
    [HttpGet("/auth/setup/status")]
    public async Task<IActionResult> SetupStatus()
    {
        var unconfigured = await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted);
        return Ok(new { configured = !unconfigured, needsAdmin = unconfigured });
    }

    /// <summary>
    /// Non-browser first-run setup (install CLI / Terraform). JSON only; no antiforgery (there is no
    /// form session). Fails closed once any Owner with a real password exists — same as the form path.
    /// Does not set a cookie (install is headless); the operator signs in at /login afterward.
    /// </summary>
    [HttpPost("/auth/setup/cli")]
    public async Task<IActionResult> SetupCli([FromBody] SetupCliRequest? body)
    {
        if (!await _auth.IsUnconfiguredAsync(HttpContext.RequestAborted))
            return Conflict(new { error = "Admin already configured." });

        if (body is null || string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@'))
            return BadRequest(new { error = "Enter a valid email address." });

        var policyError = PasswordPolicy.Validate(body.Password, body.ConfirmPassword ?? body.Password);
        if (policyError is not null)
            return BadRequest(new { error = policyError });

        var admin = await _auth.CreateFirstAdminAsync(
            body.Email, body.DisplayName ?? "", body.Password ?? "", HttpContext.RequestAborted);
        if (admin is null)
            return Conflict(new { error = "Could not create admin (already configured or email taken)." });

        return Ok(new { email = admin.Email, displayName = admin.DisplayName, role = admin.Role.ToString() });
    }

    public sealed record SetupCliRequest(string Email, string? DisplayName, string Password, string? ConfirmPassword);

    // Password login. Always shows the same generic error on failure (unknown email, wrong password, or a
    // member with no password set) so a response can't be used to enumerate accounts, and applies a small
    // fixed delay on failure (see LoginThrottle) so a naive brute-force loop can't run at line rate.
    [HttpPost("/auth/login")]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] string? returnUrl)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect("/login?error=" + Uri.EscapeDataString("Your session expired — please try again."));

        var user = await _auth.ValidateCredentialsAsync(email ?? "", password ?? "", HttpContext.RequestAborted);
        if (user is null)
        {
            await LoginThrottle.PenalizeAsync(email ?? "");
            return Redirect(BuildLoginErrorUrl("Invalid email or password.", returnUrl, email));
        }

        LoginThrottle.Clear(email ?? "");
        await SignInAsync(HttpContext, user);
        return Redirect(LocalOrHome(returnUrl));
    }

    private async Task<bool> ValidAntiforgeryAsync()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    // Carries the entered email (not the password) back onto the login form so a mistyped password
    // doesn't also force retyping the email — an email address in a redirect URL isn't sensitive the
    // way a password would be.
    private static string BuildLoginErrorUrl(string error, string? returnUrl, string? email)
    {
        var url = "/login?error=" + Uri.EscapeDataString(error);
        if (!string.IsNullOrEmpty(returnUrl))
            url += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        if (!string.IsNullOrEmpty(email))
            url += "&email=" + Uri.EscapeDataString(email);
        return url;
    }

    // Invite acceptance (join page).
    [HttpGet("/join")]
    public async Task<IActionResult> Join(string? token, string? error)
    {
        var info = string.IsNullOrEmpty(token) ? null : await _members.GetInviteAsync(token, HttpContext.RequestAborted);
        if (info is null)
            return Content(AuthPages.JoinInvalid(), "text/html");
        return Content(AuthPages.Join(_antiforgery.GetAndStoreTokens(HttpContext), token!, info, error), "text/html");
    }

    [HttpPost("/auth/accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromForm] string token, [FromForm] string password, [FromForm] string? displayName)
    {
        if (!await ValidAntiforgeryAsync())
            return Redirect($"/join?token={Uri.EscapeDataString(token ?? "")}&error=" + Uri.EscapeDataString("Your session expired — please try again."));

        var policyError = PasswordPolicy.Validate(password);
        if (policyError is not null)
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString(policyError));

        AuthUser? user;
        try
        {
            user = await _members.AcceptInviteAsync(token, displayName ?? "", password!, HttpContext.RequestAborted);
        }
        catch (ArgumentException ex)
        {
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString(ex.Message));
        }
        if (user is null)
            return Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString("This invite is invalid, used, expired, or the email is already a member."));
        await SignInAsync(HttpContext, user);
        return Redirect("/");
    }

    [HttpPost("/auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/locked");
    }

    // Signs the user into the cookie scheme with their identity + tenant + role claims.
    private static Task SignInAsync(HttpContext ctx, AuthUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("tenant", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("role", user.Role.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    // Guards against open redirects: only a same-site relative path (e.g. "/connect/authorize?…") is
    // honoured as a post-login destination; anything absolute or protocol-relative falls back to home.
    private static string LocalOrHome(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
            && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
                ? returnUrl : "/";
}
