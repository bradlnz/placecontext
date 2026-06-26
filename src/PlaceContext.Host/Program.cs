using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using PlaceContext.Host;
using PlaceContext.Host.Auth;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components;
using PlaceContext.Host.Tenancy;
using PlaceContext.Host.Tools;
using PlaceContext.Infrastructure;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

// PlaceContext is a single hosted web app on http://localhost:7700, serving two surfaces from one
// process and the same Postgres store:
//   • the Blazor portal (codebase-visibility UI) at the site root — behind cookie login, and
//   • the MCP server over Streamable HTTP at /mcp (anonymous for now; tenant from subdomain).

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Load the shared OAuth signing key (so every replica signs/verifies MCP tokens with the same RSA key).
PlaceContext.Host.Auth.OAuthKeys.Init(builder.Configuration["PlaceContext:OAuth:SigningKeyPem"]);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<PlaceContext.Host.PortalUiState>();

// Share the Data Protection key ring across replicas (persisted in Postgres) and pin the application
// name, so the auth cookie one replica issues can be decrypted by any other — otherwise a token sign-in
// handled by pod A produces a cookie pod B can't read, bouncing the operator back to /locked.
builder.Services.AddDataProtection()
    .SetApplicationName("placecontext")
    .PersistKeysToDbContext<PlaceContext.Infrastructure.Persistence.AppDbContext>();

// Cookie authentication. The portal requires a logged-in user (fallback policy); /login, /register,
// the auth endpoints, and /mcp opt out explicitly.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "placecontext_auth";
        // No password login: an unauthenticated request is sent to /locked, which tells the operator to
        // open the portal from the pctl TUI (the TUI mints a token and signs them in via /auth/portal).
        o.LoginPath = "/locked";
        o.LogoutPath = "/auth/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    })
    // Bearer tokens for MCP, issued by the first-party OAuth server (signed with the in-process RSA key).
    .AddJwtBearer(o =>
    {
        // Keep the JWT's raw claim names ("sub", "role", "tenant"). Default-true mapping renames "sub"
        // to ClaimTypes.NameIdentifier and "role" to ClaimTypes.Role, which would make the FindFirst("sub")
        // subject check below (and the role-claim refresh) silently see null and reject every valid token.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,           // issuer is per-tenant subdomain
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = OAuthKeys.SigningKey,
            ValidateLifetime = true,
            NameClaimType = "sub",
            RoleClaimType = "role",
        };
        o.Events = new JwtBearerEvents
        {
            // On a missing/invalid token, point the client at this host's protected-resource metadata.
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                var b = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate =
                    $"Bearer resource_metadata=\"{b}/.well-known/oauth-protected-resource\"";
                return Task.CompletedTask;
            },
            // The token's tenant must match the subdomain it's used on — no cross-tenant token reuse.
            OnTokenValidated = async ctx =>
            {
                var current = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
                var claim = ctx.Principal?.FindFirst("tenant")?.Value;
                if (claim is null || !Guid.TryParse(claim, out var tid) || tid != current.TenantId)
                {
                    ctx.Fail("Token tenant does not match this workspace.");
                    return;
                }

                // The JWT is self-contained, but the user it names must still exist: a DB reseed or a
                // removed member leaves 'ghost' tokens that would otherwise keep their embedded role.
                // Reject ghosts, and refresh the role claim from the DB so promotions/demotions take
                // effect without re-issuing the token.
                if (!Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var userId))
                {
                    ctx.Fail("Token has no valid subject.");
                    return;
                }
                var members = ctx.HttpContext.RequestServices.GetRequiredService<IMembershipService>();
                var dbRole = await members.GetRoleAsync(userId, ctx.HttpContext.RequestAborted);
                if (dbRole is null)
                {
                    ctx.Fail("Token user no longer exists in this workspace — re-authorize to mint a fresh token.");
                    return;
                }
                if (ctx.Principal?.Identity is ClaimsIdentity id &&
                    !string.Equals(dbRole, id.FindFirst("role")?.Value, StringComparison.OrdinalIgnoreCase))
                {
                    if (id.FindFirst("role") is { } stale) id.RemoveClaim(stale);
                    id.AddClaim(new Claim("role", dbRole));
                }
            },
        };
    });
builder.Services.AddAuthorization(o =>
{
    // Any authenticated member can read (Viewer+).
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    // Role-gated policies, used on portal management endpoints and MCP write tools.
    o.AddPolicy("Member", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Member)));
    o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Admin)));
    o.AddPolicy("Owner", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Owner)));
});
builder.Services.AddSingleton<OAuthStore>();
builder.Services.AddSingleton<PlaceContext.Host.Auth.PortalToken>();

// Exposes the current request's ClaimsPrincipal to MCP tools (e.g. whoami reads the bearer's claims).
builder.Services.AddHttpContextAccessor();

// Tenancy: per-request/circuit holder + a circuit handler that keeps interactive renders tenant-scoped.
builder.Services.AddScoped<TenantHolder>();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();

// MCP over Streamable HTTP, exposed below at /mcp.
builder.Services
    .AddMcpServer()
    // Stateless transport: each request is self-contained, so no in-memory session is pinned to a pod.
    // With >1 replica a stateful session lives on one pod and a reconnect that lands on another 404s;
    // stateless lets any replica serve any request (and survives restarts/rollouts).
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<PlaceContextTools>()
    .WithPrompts<PlaceContextPrompts>()
    .AddAuthorizationFilters(); // enforce [Authorize] on tools/prompts against the bearer token's role

// Honor ASPNETCORE_URLS when set (containers bind http://+:7700 so the k8s Service/Ingress can reach
// the pod); default to localhost:7700 for local dev.
var bindUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
builder.WebHost.UseUrls(string.IsNullOrWhiteSpace(bindUrls) ? "http://localhost:7700" : bindUrls);

var app = builder.Build();
PlaceContext.Infrastructure.DependencyInjection.MigrateDatabase(app.Services);

// Self-host activation: validate the configured key once and log the result.
var activation = app.Services.GetRequiredService<PlaceContext.Application.Ports.IActivationService>();
var activationInfo = activation.Current;
app.Logger.LogInformation("Activation: {Status} — {Reason}{Org}",
    activationInfo.Status, activationInfo.Reason,
    activationInfo.Organisation is { } org ? $" (org: {org})" : "");
if (activation.Enforced && !activationInfo.IsActive)
    app.Logger.LogWarning("Activation is ENFORCED and the deployment is not active — the MCP surface is blocked until a valid key is configured.");

app.UseStaticFiles();
app.UseMiddleware<TenantResolutionMiddleware>(); // resolve {user}.placecontext.ai → tenant, before any data access
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Enforcement: when activation is enforced and not active, block the MCP product surface (the portal
// stays reachable so the operator can see the activation status and fix it).
app.Use(async (ctx, next) =>
{
    if (activation.Enforced && !activation.Current.IsActive
        && (ctx.Request.Path.StartsWithSegments("/mcp") || ctx.Request.Path.StartsWithSegments("/ingest")))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync($"PlaceContext is not activated: {activation.Current.Reason}");
        return;
    }
    await next();
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// MCP requires an OAuth bearer token (validated by the JwtBearer scheme); the token binds the tenant.
app.MapMcp("/mcp").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

// First-party OAuth 2.1 authorization server (authorize/token/register/jwks + metadata).
app.MapOAuthServer();

// ---- External event ingress ----
// An external source (a form on a site, a Cloudflare Queue consumer, a webhook) POSTs an event here;
// it is emitted into this tenant (resolved by subdomain) and fires any subscribed event-triggers, with
// the JSON body injected as the triggered runs' input payload. Gated by a shared ingest key
// (PlaceContext:Ingest:Key); disabled when no key is configured to avoid an open relay.
app.MapPost("/ingest/{eventName}", async (HttpContext ctx, PlaceContext.Application.IPlaceContextService svc,
    IConfiguration config, string eventName, Guid? projectId) =>
{
    var configuredKey = config["PlaceContext:Ingest:Key"];
    if (string.IsNullOrWhiteSpace(configuredKey))
        return Results.StatusCode(StatusCodes.Status404NotFound);

    var presented = ctx.Request.Headers["X-Ingest-Key"].ToString();
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(configuredKey)))
        return Results.Unauthorized();

    string? payload = null;
    if (ctx.Request.ContentLength is > 0)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        payload = await reader.ReadToEndAsync(ctx.RequestAborted);
    }

    var result = await svc.EmitEventAsync(eventName, projectId, payload, ctx.RequestAborted);
    return Results.Ok(new { result.Name, result.TriggeredRuns, result.OccurredAt });
}).AllowAnonymous();

// ---- Token sign-in (self-hosted; the pctl TUI mints the token and opens /auth/portal) ----
// The portal has no password login. A valid short-lived token (HMAC-signed with the shared
// PlaceContext:Portal:SigningKey) signs the cluster operator into the cookie. In Development with no
// key configured, sign-in is automatic so `./run.sh` + opening localhost just works with no cluster.
var portalSigningKey = builder.Configuration["PlaceContext:Portal:SigningKey"];
var devAutoLogin = app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(portalSigningKey);

app.MapGet("/auth/portal", async (HttpContext ctx, IAuthService auth, PlaceContext.Host.Auth.PortalToken portal,
    string? token, string? returnUrl) =>
{
    if (!devAutoLogin && !portal.TryValidate(token, portalSigningKey, DateTimeOffset.UtcNow))
        return Results.Redirect("/locked");
    var operatorUser = await auth.GetOrCreateOperatorAsync(ctx.RequestAborted);
    await SignInAsync(ctx, operatorUser);
    return Results.Redirect(LocalOrHome(returnUrl));
}).AllowAnonymous();

// Shown when an unauthenticated request hits a protected page (cookie LoginPath). In dev it just
// signs you in; otherwise it points the operator at the TUI.
app.MapGet("/locked", async (HttpContext ctx, IAuthService auth) =>
{
    if (devAutoLogin)
    {
        var operatorUser = await auth.GetOrCreateOperatorAsync(ctx.RequestAborted);
        await SignInAsync(ctx, operatorUser);
        return Results.Redirect("/");
    }
    return Results.Content(AuthPages.Locked(), "text/html");
}).AllowAnonymous();

// ---- Invite acceptance (join page) ----
app.MapGet("/join", async (HttpContext ctx, IAntiforgery af, IMembershipService members, string? token, string? error) =>
{
    var info = string.IsNullOrEmpty(token) ? null : await members.GetInviteAsync(token, ctx.RequestAborted);
    if (info is null)
        return Results.Content(AuthPages.JoinInvalid(), "text/html");
    return Results.Content(AuthPages.Join(af.GetAndStoreTokens(ctx), token!, info, error), "text/html");
}).AllowAnonymous();

app.MapPost("/auth/accept-invite", async (HttpContext ctx, IMembershipService members,
    [FromForm] string token, [FromForm] string password, [FromForm] string? displayName) =>
{
    if ((password?.Length ?? 0) < 8)
        return Results.Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString("Choose a password of at least 8 characters."));
    var user = await members.AcceptInviteAsync(token, displayName ?? "", password!, ctx.RequestAborted);
    if (user is null)
        return Results.Redirect($"/join?token={Uri.EscapeDataString(token)}&error=" + Uri.EscapeDataString("This invite is invalid, used, or the email is already a member."));
    await SignInAsync(ctx, user);
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/locked");
}).AllowAnonymous();

// ---- GitHub OAuth (repo import) — requires a logged-in user (covered by the fallback policy) ----
app.MapGet("/auth/github/login", (HttpContext ctx, IGitHubGateway gh) =>
{
    if (!gh.IsConfigured)
        return Results.Content("GitHub OAuth is not configured. Set PlaceContext:GitHub:ClientId and ClientSecret.", "text/plain");

    var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/github/callback";
    var state = Guid.NewGuid().ToString("N");
    ctx.Response.Cookies.Append("gh_state", state, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
    return Results.Redirect(gh.BuildAuthorizeUrl(redirectUri, state));
});

app.MapGet("/auth/github/callback", async (HttpContext ctx, IGitHubGateway gh, ITenantStore tenants, ICurrentTenant tenant, string? code, string? state) =>
{
    var expected = ctx.Request.Cookies["gh_state"];
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || !string.Equals(state, expected, StringComparison.Ordinal))
        return Results.BadRequest("Invalid OAuth state.");

    var redirectUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/github/callback";
    var token = await gh.ExchangeCodeAsync(code, redirectUri, ctx.RequestAborted);
    if (token is null)
        return Results.BadRequest("GitHub token exchange failed.");

    var user = await gh.GetUserAsync(token, ctx.RequestAborted);
    await tenants.SaveGitHubAsync(tenant.TenantId, user?.Login ?? string.Empty, token, ctx.RequestAborted);
    ctx.Response.Cookies.Delete("gh_state");
    return Results.Redirect("/import");
});

await app.RunAsync();

// Signs the user into the cookie scheme with their identity + tenant + role claims.
static Task SignInAsync(HttpContext ctx, AuthUser user)
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

// Reads the principal's role (from either the cookie's ClaimTypes.Role or the JWT's "role" claim) and
// returns whether it meets the minimum. Backs the Member/Admin/Owner authorization policies.
static bool RoleAtLeast(ClaimsPrincipal user, UserRole min)
{
    var value = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
    return Enum.TryParse<UserRole>(value, out var role) && role >= min;
}

// Guards against open redirects: only a same-site relative path (e.g. "/connect/authorize?…") is
// honoured as a post-login destination; anything absolute or protocol-relative falls back to home.
static string LocalOrHome(string? returnUrl) =>
    !string.IsNullOrEmpty(returnUrl)
        && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
        && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl : "/";
