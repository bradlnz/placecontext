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
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// PlaceContext is a single hosted web app on http://localhost:7700, serving two surfaces from one
// process and the same Postgres store:
//   • the Blazor portal (codebase-visibility UI) at the site root — behind cookie login, and
//   • the MCP server over Streamable HTTP at /mcp (anonymous for now; tenant from subdomain).

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// OpenTelemetry: traces + metrics for the jobs pipeline (and ASP.NET/runtime), giving a realtime,
// exportable view into runs. Emits over OTLP to the endpoint in PlaceContext:Otel:Endpoint (or the
// standard OTEL_EXPORTER_OTLP_ENDPOINT). With no endpoint configured the SDK still collects but has
// nowhere to push, so the instrumentation is effectively free until a collector is pointed at it.
{
    var otlpEndpoint = builder.Configuration["PlaceContext:Otel:Endpoint"]
        ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    var serviceName = builder.Configuration["PlaceContext:Otel:ServiceName"] ?? "placecontext";
    var otel = builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(t =>
        {
            t.AddSource(PlaceContext.Application.Observability.JobTelemetry.SourceName)
             .AddAspNetCoreInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        })
        .WithMetrics(m =>
        {
            m.AddMeter(PlaceContext.Application.Observability.JobTelemetry.SourceName)
             .AddAspNetCoreInstrumentation()
             .AddRuntimeInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });
}

// Load the shared OAuth signing key (so every replica signs/verifies MCP tokens with the same RSA key).
PlaceContext.Host.Auth.OAuthKeys.Init(builder.Configuration["PlaceContext:OAuth:SigningKeyPem"]);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Compress dynamic responses (the initial Blazor HTML document is the portal's biggest payload —
// inline CSS included — and it currently ships uncompressed). Brotli first, gzip fallback. The
// Host terminates plain HTTP in-cluster (TLS ends at Traefik), so the default no-compression-
// over-HTTPS stance never disables it in practice.
builder.Services.AddResponseCompression(o =>
{
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
// The former minimal-API endpoints (ingest, backup, auth, artifacts, health) now live as controllers
// under Controllers/ — attribute-routed, same paths/auth, wired below with MapControllers().
builder.Services.AddControllers();
// One shared in-memory cache for expensive read models (the per-project dependency graph above all).
builder.Services.AddMemoryCache();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<PlaceContext.Host.PortalUiState>();
builder.Services.AddScoped<PlaceContext.Host.Branding.BrandingService>();

// Share the Data Protection key ring across replicas (persisted in Postgres) and pin the application
// name, so the auth cookie one replica issues can be decrypted by any other — otherwise a token sign-in
// handled by pod A produces a cookie pod B can't read, bouncing the operator back to /locked.
// When PlaceContext:DataProtection:Key is set, the ring is encrypted at rest with that passphrase so a
// DB dump alone cannot decrypt vault secrets or auth cookies.
{
    var dpBuilder = builder.Services.AddDataProtection()
        .SetApplicationName("placecontext")
        .PersistKeysToDbContext<PlaceContext.Infrastructure.Persistence.AppDbContext>();
    var dpKey = builder.Configuration["PlaceContext:DataProtection:Key"];
    // Outside Development a passphrase is required so a Postgres dump cannot decrypt vault secrets
    // or auth cookies. Dev may omit it for zero-config local runs.
    if (string.IsNullOrWhiteSpace(dpKey) && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "PlaceContext:DataProtection:Key must be set outside Development (shared passphrase encrypting the Data Protection key ring).");
    if (!string.IsNullOrWhiteSpace(dpKey))
    {
        var encryptor = new PlaceContext.Infrastructure.Security.PassphraseXmlEncryptor(dpKey);
        dpBuilder.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.XmlEncryption.IXmlEncryptor>(encryptor);
        dpBuilder.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.XmlEncryption.IXmlDecryptor>(encryptor);
        // Wire the encryptor into the key management options.
        dpBuilder.Services.Configure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(o =>
        {
            o.XmlEncryptor = encryptor;
        });
    }
}

// Cookie authentication. The portal requires a logged-in user (fallback policy); /login, /register,
// the auth endpoints, and /mcp opt out explicitly.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "placecontext_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
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
                var b = PlaceContext.Host.Tenancy.PublicUrl.Base(ctx.HttpContext, ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
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
    })
    // Machine-facing "ApiKey" scheme for the /api/v1/* management API (the Terraform provider and other
    // IaC/CI clients). Deliberately opt-in per endpoint via [Authorize(AuthenticationSchemes = "ApiKey")]
    // — it never becomes the ambient default, so it can't accidentally widen the portal or MCP surfaces.
    .AddScheme<PlaceContext.Host.Auth.ApiKeyAuthenticationOptions, PlaceContext.Host.Auth.ApiKeyAuthenticationHandler>(
        PlaceContext.Host.Auth.ApiKeyAuthenticationHandler.SchemeName, _ => { })
    // Personal user API tokens (Settings → API tokens), used by the entity data API at /api/v1/data/*.
    .AddScheme<PlaceContext.Host.Auth.UserApiTokenAuthenticationOptions, PlaceContext.Host.Auth.UserApiTokenAuthenticationHandler>(
        PlaceContext.Host.Auth.UserApiTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(o =>
{
    // Any authenticated member can read (Viewer+).
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    // Role-gated policies, used on portal management endpoints and MCP write tools.
    o.AddPolicy("Member", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Member)));
    o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Admin)));
    o.AddPolicy("Owner", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Owner)));
    // Fine-grained permission policies — the policy name IS the permission string (see the Permission
    // catalog), so gating a new sensitive tool/endpoint/page is just [Authorize(Policy = Permission.X)].
    // Backed by PermissionAuthorizationHandler, which resolves role defaults + tenant-scoped overrides.
    foreach (var permission in PlaceContext.Application.Ports.Permission.All)
        o.AddPolicy(permission, p => p.RequireAuthenticatedUser()
            .AddRequirements(new PlaceContext.Host.Auth.PermissionRequirement(permission)));
});
// Scoped, not singleton: it depends on the scoped IPermissionService (which in turn depends on the
// scoped IUserPermissionGrantRepository / DbContext).
builder.Services.AddScoped<IAuthorizationHandler, PlaceContext.Host.Auth.PermissionAuthorizationHandler>();
builder.Services.AddSingleton<OAuthStore>();
builder.Services.AddSingleton<PlaceContext.Host.Auth.PortalToken>();

// Exposes the current request's ClaimsPrincipal to MCP tools (e.g. whoami reads the bearer's claims).
builder.Services.AddHttpContextAccessor();

// Tenancy: per-request/circuit holder + a circuit handler that keeps interactive renders tenant-scoped.
builder.Services.AddScoped<TenantHolder>();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();

// Granular RBAC: per-request/circuit holder + a circuit handler that keeps interactive renders
// resolved to the right caller (mirrors the tenant holder/handler pair above).
builder.Services.AddScoped<PlaceContext.Host.Auth.UserHolder>();
builder.Services.AddScoped<CircuitHandler, PlaceContext.Host.Auth.UserCircuitHandler>();

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

// Hard caps against memory exhaustion / large-body tricks (oversized uploads, zip bombs via body,
// multi-GB artifact POSTs). Field encryption also enforces its own max plaintext size.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 32L * 1024 * 1024; // 32 MiB
    o.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
    o.Limits.MaxRequestLineSize = 16 * 1024;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 32L * 1024 * 1024;
    o.ValueLengthLimit = 16 * 1024 * 1024;
    o.MemoryBufferThreshold = 256 * 1024; // spill to disk beyond 256 KiB
});

var app = builder.Build();
PlaceContext.Infrastructure.DependencyInjection.MigrateDatabase(app.Services);
// Encrypt any legacy plaintext (and migrate knowledge context DB → encrypted files) before serving.
await PlaceContext.Infrastructure.DependencyInjection.EncryptExistingDataAsync(app.Services);

// Subscriptions/billing are handled by a separate web portal (the TUI sends users there to pay), so the
// product is no longer gated by an activation key.

app.UseResponseCompression();
app.UseStaticFiles(new StaticFileOptions
{
    // The vendored scripts (chart.umd.min.js, pcmonaco.js, pcgraph.js) and CSS were revalidated on
    // every page view. An hour of client caching removes that chatter without pinning upgrades:
    // assets are not fingerprinted, so keep the window modest rather than immutable.
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = "public,max-age=3600",
});
app.Use(async (ctx, next) =>
{
    // Baseline security headers for every response (portal, API, MCP).
    ctx.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    // SAMEORIGIN (not DENY): the Artifacts viewer embeds /runs/…/artifacts/… in a same-origin
    // iframe. DENY blocks that even for the portal's own host — breaks previews on any public DNS
    // hostname (and localhost). Still prevents third-party clickjacking.
    ctx.Response.Headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
    ctx.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    ctx.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});
app.UseMiddleware<TenantResolutionMiddleware>(); // resolve {user}.placecontext.ai → tenant, before any data access
// Project for the entity data API: X-Project-Id / X-Project (optional; entity routes require it).
app.UseMiddleware<PlaceContext.Host.Tenancy.ProjectResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
// After UseAuthorization deliberately — see UserResolutionMiddleware for why.
app.UseMiddleware<PlaceContext.Host.Auth.UserResolutionMiddleware>();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// MCP requires an OAuth bearer token (validated by the JwtBearer scheme); the token binds the tenant.
app.MapMcp("/mcp").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

// First-party OAuth 2.1 authorization server (authorize/token/register/jwks + metadata).
app.MapOAuthServer();

// Ingest/backup/artifact/auth/health endpoints now live as attribute-routed controllers under
// Controllers/ (same paths, same [Authorize]/[AllowAnonymous] as the minimal APIs they replace).
app.MapControllers();

await app.RunAsync();

// Reads the principal's role (from either the cookie's ClaimTypes.Role or the JWT's "role" claim) and
// returns whether it meets the minimum. Backs the Member/Admin/Owner authorization policies.
static bool RoleAtLeast(ClaimsPrincipal user, UserRole min)
{
    var value = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
    return Enum.TryParse<UserRole>(value, out var role) && role >= min;
}
