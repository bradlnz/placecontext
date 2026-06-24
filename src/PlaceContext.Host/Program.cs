using System.Security.Claims;
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

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<PlaceContext.Host.PortalUiState>();

// Cookie authentication. The portal requires a logged-in user (fallback policy); /login, /register,
// the auth endpoints, and /mcp opt out explicitly.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "placecontext_auth";
        o.LoginPath = "/login";
        o.LogoutPath = "/auth/logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    })
    // Bearer tokens for MCP, issued by the first-party OAuth server (signed with the in-process RSA key).
    .AddJwtBearer(o =>
    {
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
            OnTokenValidated = ctx =>
            {
                var current = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
                var claim = ctx.Principal?.FindFirst("tenant")?.Value;
                if (claim is null || !Guid.TryParse(claim, out var tid) || tid != current.TenantId)
                    ctx.Fail("Token tenant does not match this workspace.");
                return Task.CompletedTask;
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

// Tenancy: per-request/circuit holder + a circuit handler that keeps interactive renders tenant-scoped.
builder.Services.AddScoped<TenantHolder>();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();

// MCP over Streamable HTTP, exposed below at /mcp.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<PlaceContextTools>()
    .WithPrompts<PlaceContextPrompts>()
    .AddAuthorizationFilters(); // enforce [Authorize] on tools/prompts against the bearer token's role

builder.WebHost.UseUrls("http://localhost:7700");

var app = builder.Build();
PlaceContext.Infrastructure.DependencyInjection.MigrateDatabase(app.Services);

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

// ---- Login / register (standalone HTML so they don't depend on the Blazor render pipeline) ----
app.MapGet("/login", (HttpContext ctx, IAntiforgery af, string? error) =>
    Results.Content(AuthPages.Login(af.GetAndStoreTokens(ctx), error), "text/html")).AllowAnonymous();

app.MapGet("/register", (HttpContext ctx, IAntiforgery af, string? error) =>
    Results.Content(AuthPages.Register(af.GetAndStoreTokens(ctx), error), "text/html")).AllowAnonymous();

// Self-registration creates the organisation's FIRST member as Owner. After that the org is
// invite-only — further members join via an Admin invite (/join?token=…).
app.MapPost("/auth/register", async (HttpContext ctx, IAuthService auth,
    [FromForm] string email, [FromForm] string password, [FromForm] string? displayName) =>
{
    if (await auth.HasAnyMembersAsync(ctx.RequestAborted))
        return Results.Redirect("/register?error=" + Uri.EscapeDataString("This organisation is invite-only — ask an admin to invite you."));
    if (string.IsNullOrWhiteSpace(email) || (password?.Length ?? 0) < 8)
        return Results.Redirect("/register?error=" + Uri.EscapeDataString("Enter an email and a password of at least 8 characters."));
    var user = await auth.RegisterAsync(email, displayName ?? "", password!, UserRole.Owner, ctx.RequestAborted);
    if (user is null)
        return Results.Redirect("/register?error=" + Uri.EscapeDataString("That email is already registered."));
    await SignInAsync(ctx, user);
    return Results.Redirect("/");
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

app.MapPost("/auth/login", async (HttpContext ctx, IAuthService auth,
    [FromForm] string email, [FromForm] string password) =>
{
    var user = await auth.ValidateAsync(email, password, ctx.RequestAborted);
    if (user is null)
        return Results.Redirect("/login?error=" + Uri.EscapeDataString("Incorrect email or password."));
    await SignInAsync(ctx, user);
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
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
