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
builder.Services.AddScoped<PlaceContext.Host.Branding.BrandingService>();
builder.Services.AddScoped<PlaceContext.Host.Demo.BrisbaneDemoSeeder>();

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

// Subscriptions/billing are handled by a separate web portal (the TUI sends users there to pay), so the
// product is no longer gated by an activation key.

app.UseStaticFiles();
app.UseMiddleware<TenantResolutionMiddleware>(); // resolve {user}.placecontext.ai → tenant, before any data access
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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

// ---- Demo seed ----
// Seed the Brisbane property feasibility demo into the resolved tenant: project + Data tables +
// decisions + context, with the analytics sweep queued. Same gate as /ingest
// (shared key, disabled when unconfigured); the Onboarding page has the same action as a button.
app.MapPost("/seed/brisbane-demo", async (HttpContext ctx, IConfiguration config,
    PlaceContext.Host.Demo.BrisbaneDemoSeeder seeder) =>
{
    var configuredKey = config["PlaceContext:Ingest:Key"];
    if (string.IsNullOrWhiteSpace(configuredKey))
        return Results.StatusCode(StatusCodes.Status404NotFound);
    var presented = ctx.Request.Headers["X-Ingest-Key"].ToString();
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(configuredKey)))
        return Results.Unauthorized();

    var tenant = PlaceContext.Infrastructure.Tenancy.CurrentTenant.Current;
    if (tenant is null) return Results.BadRequest(new { error = "no tenant resolved" });

    var (projectId, already) = await seeder.SeedAsync(tenant, ctx.RequestAborted);
    return Results.Ok(new { projectId, alreadySeeded = already, project = $"/project/{projectId}" });
}).AllowAnonymous();

// ---- Inbound SMS gateway ----
// A delivery provider (Twilio-style form post) or any bridge (JSON) POSTs inbound texts here; the
// tenant comes from the subdomain (same middleware as /ingest). The sender and body are encrypted
// before storage and a sms.received event fires (metadata only) for triggers. Gated by a shared
// key (PlaceContext:Sms:InboundKey) passed as ?key= or X-Sms-Key; disabled when unconfigured.
app.MapPost("/sms/inbound", async (HttpContext ctx, PlaceContext.Application.IPlaceContextService svc,
    IConfiguration config, Guid? projectId) =>
{
    var configuredKey = config["PlaceContext:Sms:InboundKey"];
    if (string.IsNullOrWhiteSpace(configuredKey))
        return Results.StatusCode(StatusCodes.Status404NotFound);

    var presented = ctx.Request.Headers["X-Sms-Key"].ToString();
    if (string.IsNullOrEmpty(presented)) presented = ctx.Request.Query["key"].ToString();
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented),
            System.Text.Encoding.UTF8.GetBytes(configuredKey)))
        return Results.Unauthorized();

    string from, to, body, provider;
    string? externalId;
    if (ctx.Request.HasFormContentType)
    {
        // Twilio-compatible form fields.
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        from = form["From"].ToString();
        to = form["To"].ToString();
        body = form["Body"].ToString();
        externalId = form["MessageSid"].ToString() is { Length: > 0 } sid ? sid : null;
        provider = "twilio";
    }
    else
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
        var root = doc.RootElement;
        string Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()! : "";
        from = Get("from"); to = Get("to"); body = Get("body");
        externalId = Get("externalId") is { Length: > 0 } eid ? eid : null;
        provider = Get("provider") is { Length: > 0 } p ? p : "generic";
    }

    try
    {
        await svc.ReceiveInboundSmsAsync(
            new PlaceContext.Application.Features.ReceiveInboundSmsCommand(from, to, body, provider, externalId, projectId),
            ctx.RequestAborted);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    // Twilio expects TwiML back on form posts; an empty <Response/> means "received, no reply".
    return ctx.Request.HasFormContentType
        ? Results.Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml")
        : Results.Ok(new { received = true });
}).AllowAnonymous();

// ---- Run artifacts (post-job outputs) — stream an artifact from the object store (MinIO) ----
// The portal/TUI link here; the IRunArtifactLinkRepository tenant filter scopes the lookup to the
// signed-in tenant, so one tenant can't read another's artifacts. HTML, images, and PDFs render
// inline (the browser previews them in the tab); the rest download with their original filename.
app.MapGet("/runs/{runId:guid}/artifacts/{artifactId:guid}", async (
    Guid runId, Guid artifactId, HttpContext ctx,
    PlaceContext.Domain.Repositories.IRunArtifactLinkRepository links,
    PlaceContext.Application.Ports.IObjectStore store) =>
{
    var link = await links.GetByIdAsync(artifactId, ctx.RequestAborted);
    if (link is null || link.RunId != runId) return Results.NotFound();
    var obj = await store.OpenReadAsync(link.Bucket, link.ObjectKey, ctx.RequestAborted);
    if (obj is null) return Results.NotFound();

    var inline = obj.ContentType.StartsWith("text/html") || obj.ContentType.StartsWith("image/")
        || obj.ContentType.StartsWith("application/pdf");
    var fileName = inline ? null : link.ObjectKey[(link.ObjectKey.LastIndexOf('/') + 1)..];
    return Results.Stream(obj.Content, obj.ContentType, fileDownloadName: fileName);
}).RequireAuthorization();

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

// Auto-login: an unauthenticated request to a protected page (cookie LoginPath) signs straight in
// as the operator's default workspace — the portal has no login screen. The cookie/tenant
// machinery stays intact underneath, so tenant isolation, invites, and MCP OAuth keep working.
// Probes need a cookieless 200 — auto-login turned "/" into a redirect dance that kubelet
// treats as failure after 10 hops.
app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

app.MapGet("/locked", async (HttpContext ctx, IAuthService auth) =>
{
    var operatorUser = await auth.GetOrCreateOperatorAsync(ctx.RequestAborted);
    await SignInAsync(ctx, operatorUser);
    // Honour the cookie middleware's ReturnUrl so deep links survive the auto-login round trip.
    return Results.Redirect(LocalOrHome(ctx.Request.Query["ReturnUrl"]));
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
