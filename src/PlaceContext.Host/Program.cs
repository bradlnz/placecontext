using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using PlaceContext.Host;
using PlaceContext.Host.Auth;
using PlaceContext.Application;
using PlaceContext.AgentChat;
using PlaceContext.Agents;
using PlaceContext.Artifacts;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Controllers;
using PlaceContext.Host.CoreApi;
using PlaceContext.Host.Tenancy;
using PlaceContext.Host.Tools;
using PlaceContext.Infrastructure;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Crm;
using PlaceContext.Data;
using PlaceContext.Jobs;
using PlaceContext.Search;
using PlaceContext.Vault;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PlaceContext.Host.Branding;
using PlaceContext.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using PlaceContext.Infrastructure.Persistence;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using FluentValidation;

// PlaceContext is a single hosted web app on http://localhost:7700, serving two surfaces from one
// process and the same Postgres store:
//   • the Blazor portal (codebase-visibility UI) at the site root — behind cookie login, and
//   • the MCP server over Streamable HTTP at /mcp (anonymous for now; tenant from subdomain).

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // In the container the Host project lives under {CWD}/host/, so wwwroot is at host/wwwroot/
    // rather than the default {CWD}/wwwroot. Detect and point there.
    WebRootPath = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "host", "wwwroot"))
        ? Path.Combine("host", "wwwroot")
        : null, // null = default (wwwroot)
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddApplication();
builder.Services.AddAgentChatModule();
builder.Services.AddAgentsModule();
builder.Services.AddArtifactsModule();
builder.Services.AddCrmModule();
builder.Services.AddDataModule();
builder.Services.AddJobsModule();
builder.Services.AddSearchModule();
builder.Services.AddVaultModule();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgentChatInfrastructure(builder.Configuration);
builder.Services.AddAgentsInfrastructure(builder.Configuration);
builder.Services.AddJobsInfrastructure(builder.Configuration);
builder.Services.AddCrmInfrastructure();
builder.Services.AddArtifactsInfrastructure(builder.Configuration);
builder.Services.AddDataInfrastructure();
builder.Services.AddSearchInfrastructure(builder.Configuration);
builder.Services.AddVaultInfrastructure(builder.Configuration);

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
OAuthKeys.Init(builder.Configuration["PlaceContext:OAuth:SigningKeyPem"]);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// The Blazor SignalR hub defaults to 32 KB for client→server messages. File-switching in the
// Monaco editor syncs content back via JS interop (getValue), so large code files (>32 KB)
// blow past the limit and kill the circuit. Raise it to 1 MB.
builder.Services.Configure<HubOptions>(o => o.MaximumReceiveMessageSize = 1 * 1024 * 1024);
// Compress dynamic responses (the initial Blazor HTML document is the portal's biggest payload —
// inline CSS included — and it currently ships uncompressed). Brotli first, gzip fallback. The
// Host terminates plain HTTP in-cluster (TLS ends at Traefik), so the default no-compression-
// over-HTTPS stance never disables it in practice.
builder.Services.AddResponseCompression(o =>
{
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
// The former minimal-API endpoints (ingest, backup, auth, artifacts, health) now live as controllers
// under Controllers/ — attribute-routed, same paths/auth, wired below with MapControllers().
builder.Services.AddControllers()
    .AddApplicationPart(typeof(PlaceContext.AgentChat.Controllers.AgentChatController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Agents.Controllers.AgentsController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Artifacts.Controllers.ArtifactsController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Crm.Controllers.CrmController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Data.Controllers.DataController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Jobs.Controllers.JobsController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Search.Controllers.SearchController).Assembly)
    .AddApplicationPart(typeof(PlaceContext.Vault.Controllers.VaultController).Assembly);
builder.Services.AddScoped<IValidator<LeadIngestionRequest>, LeadIngestionRequestValidator>();
builder.Services.AddScoped<IValidator<JsonElement>, CrmIngestionPayloadValidator>();
builder.Services.Configure<CoreApiOptions>(builder.Configuration.GetSection("PlaceContext:CoreApi"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-ingestion", context =>
    {
        // Valid webhook credentials receive independent limits. Invalid/credential-less traffic is
        // grouped by host and peer address so it cannot consume a valid integration's allowance.
        var credential = context.Request.Headers[CrmIngestionSettingsService.TokenHeader].ToString();
        if (string.IsNullOrEmpty(credential))
            credential = context.Request.Headers["X-Ingest-Key"].ToString();
        if (string.IsNullOrEmpty(credential))
            credential = context.Request.Headers["X-Slack-Signature"].ToString();
        var partition = string.IsNullOrEmpty(credential)
            ? $"{context.Request.Host.Host}:{context.Connection.RemoteIpAddress}"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
    options.AddPolicy("artifact-share", context =>
    {
        // Share credentials are deliberately high entropy. Rate-limit by caller address rather
        // than token so rotating random path values cannot bypass brute-force protection.
        var partition = $"{context.Request.Host.Host}:{context.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});
builder.Services.AddHttpClient();

// Log chat gateway config at startup
var chatSection = builder.Configuration.GetSection("PlaceContext:ClusterChat");
var chatEndpoint = chatSection["Endpoint"];
var chatModel = chatSection["Model"];
var shardEndpoints = chatSection.GetSection("ShardEndpoints").Get<List<string>>() ?? new();
Console.WriteLine($"[startup] ClusterChat.Endpoint='{chatEndpoint}'  Model='{chatModel}'  ShardEndpoints=[{string.Join(", ", shardEndpoints)}]");
Console.WriteLine($"[startup] Chat.Endpoint='{builder.Configuration["PlaceContext:Chat:Endpoint"]}'");
// One shared in-memory cache for expensive read models (the per-project dependency graph above all).
builder.Services.AddMemoryCache();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<PortalUiState>();
builder.Services.AddScoped<BrandingService>();

// ── Page ViewModels ──────────────────────────────────────────────────────────
// Page ViewModels are scoped to the Blazor circuit. Repeated stateful component ViewModels opt
// into IComponentViewModel and are transient so each rendered component gets isolated state.
var pageViewModelType = typeof(PageViewModel);
foreach (var viewModelType in pageViewModelType.Assembly.GetTypes()
             .Where(type => !type.IsAbstract && pageViewModelType.IsAssignableFrom(type)))
{
    if (typeof(IComponentViewModel).IsAssignableFrom(viewModelType))
        builder.Services.AddTransient(viewModelType);
    else
        builder.Services.AddScoped(viewModelType);
}

// Share the Data Protection key ring across replicas (persisted in Postgres) and pin the application
// name, so the auth cookie one replica issues can be decrypted by any other — otherwise a token sign-in
// handled by pod A produces a cookie pod B can't read, bouncing the operator back to /locked.
// When PlaceContext:DataProtection:Key is set, the ring is encrypted at rest with that passphrase so a
// DB dump alone cannot decrypt vault secrets or auth cookies.
{
    var dpBuilder = builder.Services.AddDataProtection()
        .SetApplicationName("placecontext")
        .PersistKeysToDbContext<AppDbContext>();
    var dpKey = builder.Configuration["PlaceContext:DataProtection:Key"];
    // Outside Development a passphrase is required so a Postgres dump cannot decrypt vault secrets
    // or auth cookies. Dev may omit it for zero-config local runs.
    if (string.IsNullOrWhiteSpace(dpKey) && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "PlaceContext:DataProtection:Key must be set outside Development (shared passphrase encrypting the Data Protection key ring).");
    if (!string.IsNullOrWhiteSpace(dpKey))
    {
        var encryptor = new PassphraseXmlEncryptor(dpKey);
        dpBuilder.Services.AddSingleton<IXmlEncryptor>(encryptor);
        dpBuilder.Services.AddSingleton<IXmlDecryptor>(encryptor);
        // Wire the encryptor into the key management options.
        dpBuilder.Services.Configure<KeyManagementOptions>(o =>
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
        // The Blazor SignalR hubs (/_blazor, /_blazor/negotiate, /_blazor/initializers) expect JSON
        // responses. Return 401 + empty JSON instead of a 302 redirect to /locked (HTML), which the
        // Blazor JS client would try to parse as JSON → "unexpected token" errors.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_blazor") || ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/_blazor") || ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
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
                var b = PublicUrl.Base(ctx.HttpContext, ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>());
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
    // The generic agent accepts either a real user's personal token or an OAuth access token. Route
    // each credential to exactly one validator so a valid pct_ token is not also parsed (and logged)
    // as a malformed JWT.
    .AddPolicyScheme(AgentAuthenticationDefaults.SchemeName, AgentAuthenticationDefaults.SchemeName, o =>
    {
        o.ForwardDefaultSelector = AgentAuthenticationDefaults.SelectScheme;
    })
    // Machine-facing "ApiKey" scheme for the /api/v1/* management API (the Terraform provider and other
    // IaC/CI clients). Deliberately opt-in per endpoint via [Authorize(AuthenticationSchemes = "ApiKey")]
    // — it never becomes the ambient default, so it can't accidentally widen the portal or MCP surfaces.
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { })
    // Frontend-only Core API clients (engine/API clients). This scheme is intentionally separate from
    // user cookies / MCP bearer tokens so only explicitly registered frontend applications can call
    // /api/core/*.
    .AddScheme<AuthenticationSchemeOptions, CoreApiAuthenticationHandler>(
        CoreApiAuthenticationHandler.SchemeName, _ => { })
    // Personal user API tokens (Settings → API tokens), used by the entity data API at /api/v1/data/*.
    .AddScheme<UserApiTokenAuthenticationOptions, UserApiTokenAuthenticationHandler>(
        UserApiTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(o =>
{
    // Any authenticated member can read (Viewer+).  The Blazor SignalR hubs (/_blazor,
    // /_blazor/negotiate, /_blazor/initializers) are mapped internally by
    // AddInteractiveServerRenderMode() and don't carry [AllowAnonymous], so the fallback policy
    // would reject unauthenticated requests and break Blazor initialisation on public pages like
    // /login and /setup.  A BlazorHubBypass requirement lets the handler succeed for those paths
    // so anonymous visitors can still load the portal shell.
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new BlazorHubBypass())
        .Build();
    // Role-gated policies, used on portal management endpoints and MCP write tools.
    o.AddPolicy("Member", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Member)));
    o.AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Admin)));
    o.AddPolicy("Owner", p => p.RequireAuthenticatedUser().RequireAssertion(c => RoleAtLeast(c.User, UserRole.Owner)));
    // Fine-grained permission policies — the policy name IS the permission string (see the Permission
    // catalog), so gating a new sensitive tool/endpoint/page is just [Authorize(Policy = Permission.X)].
    // Backed by PermissionAuthorizationHandler, which resolves role defaults + tenant-scoped overrides.
    foreach (var permission in PlaceContext.Application.Ports.Permission.All)
        o.AddPolicy(permission, p => p.RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
    // Default-admin-only policy — gates the /settings/* area (beyond the self-service API tokens
    // page) and the controllers backing it to the tenant's bootstrap administrator.
    o.AddPolicy(Policies.DefaultAdmin, p => p.RequireAuthenticatedUser()
        .AddRequirements(new DefaultAdminRequirement()));
    foreach (var scope in CoreApiScopes.All)
    {
        var scopeName = scope;
        o.AddPolicy(scopeName, p => p
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes(CoreApiAuthenticationHandler.SchemeName)
            .AddRequirements(new CoreApiScopeRequirement(scopeName)));
    }
});
// Scoped, not singleton: it depends on the scoped IPermissionService (which in turn depends on the
// scoped IUserPermissionGrantRepository / DbContext).
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, DefaultAdminAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CoreApiScopeAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, BlazorHubBypassHandler>();
builder.Services.AddScoped<ICoreApiResourceResolver, CoreApiResourceResolver>();
builder.Services.AddScoped<IOAuthAuthCodeStore, EfOAuthAuthCodeStore>();
builder.Services.AddSingleton<PortalToken>();

// Exposes the current request's ClaimsPrincipal to MCP tools (e.g. whoami reads the bearer's claims).
builder.Services.AddHttpContextAccessor();

// Tenancy: per-request/circuit holder + a circuit handler that keeps interactive renders tenant-scoped.
builder.Services.AddScoped<TenantHolder>();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();

// Granular RBAC: per-request/circuit holder + a circuit handler that keeps interactive renders
// resolved to the right caller (mirrors the tenant holder/handler pair above).
builder.Services.AddScoped<UserHolder>();
builder.Services.AddScoped<CircuitHandler, UserCircuitHandler>();

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

// UseForwardedHeaders is deprecated in .NET 10 and no longer sets Request.Scheme.
// Manually read X-Forwarded-Proto from Traefik so the app knows it's HTTPS.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
        && string.Equals(proto, "https", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Request.Scheme = "https";
    }
    await next();
});

PlaceContext.Infrastructure.DependencyInjection.MigrateDatabase(app.Services);
await PlaceContext.AgentChat.Infrastructure.Persistence.AgentChatDatabaseMigrationExtensions
    .MigrateAgentChatDatabaseAsync(app.Services);
await PlaceContext.Agents.Infrastructure.Persistence.AgentsDatabaseMigrationExtensions
    .MigrateAgentsDatabaseAsync(app.Services);
await PlaceContext.Artifacts.Infrastructure.Persistence.ArtifactsDatabaseMigrationExtensions
    .MigrateArtifactsDatabaseAsync(app.Services);
await PlaceContext.Vault.Infrastructure.Persistence.VaultDatabaseMigrationExtensions
    .MigrateVaultDatabaseAsync(app.Services);
// CRM records are small, and older releases stored client identity/contact fields in plaintext.
// Rewrite those legacy rows in bounded batches before accepting requests. New writes are encrypted
// in their repositories, so this normally becomes a quick no-op after the first upgraded launch.
await PlaceContext.Infrastructure.DependencyInjection.EncryptExistingCrmDataAsync(app.Services);
// Legacy JSON blob flattening is OFF by default: the data map now stores objects/arrays as JSON
// text in their declared column, so huge nested payloads don't explode into hundreds of leaf
// columns. The bootstrap remains available via PlaceContext:DataMapFlattening:BootstrapOnStartup=true
// for backfilling historically flattened tables if ever needed.
if (app.Configuration.GetValue("PlaceContext:DataMapFlattening:BootstrapOnStartup", false))
    await PlaceContext.Data.Infrastructure.ProjectData.JsonFlatteningBootstrap.RunAsync(app.Services);
else
    app.Logger.LogInformation("Data-map flattening bootstrap skipped (PlaceContext:DataMapFlattening:BootstrapOnStartup is false).");

// Encrypt any legacy plaintext before serving.
// OFF by default: the EF passes load whole Jobs/JobRuns tables (map/reduce source + full run
// artifacts, MBs per row, all tenants) into memory at once and OOM the pod. Opt in with
// PlaceContext:EncryptionAtRest:BootstrapOnStartup=true only on a host sized for the one-shot scan.
if (app.Configuration.GetValue("PlaceContext:EncryptionAtRest:BootstrapOnStartup", false))
{
    await PlaceContext.Infrastructure.DependencyInjection.EncryptExistingDataAsync(app.Services);
    await PlaceContext.Vault.Infrastructure.Security.VaultEncryptionAtRestBootstrap.RunAsync(app.Services);
}
else
    app.Logger.LogInformation("Encryption-at-rest startup bootstrap skipped (PlaceContext:EncryptionAtRest:BootstrapOnStartup is not true).");

// Subscriptions/billing are handled by a separate web portal (the TUI sends users there to pay), so the
// product is no longer gated by an activation key.

app.UseResponseCompression();
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".sh"] = "text/x-shellscript";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes,
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
// Project for the entity data and search APIs: X-Project-Id / X-Project (optional elsewhere).
app.UseMiddleware<ProjectResolutionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
// After UseAuthorization deliberately — see UserResolutionMiddleware for why.
app.UseMiddleware<UserResolutionMiddleware>();
app.UseAntiforgery();

app.MapRazorComponents<App>()
.AddInteractiveServerRenderMode()
.AllowAnonymous();

// MCP requires an OAuth bearer token (validated by the JwtBearer scheme); the token binds the tenant.
app.MapMcp("/mcp").RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

// First-party OAuth 2.1 authorization server (authorize/token/register/jwks + metadata).
app.MapOAuthServer();

// Ingest/backup/artifact/auth/health endpoints now live as attribute-routed controllers under
// Controllers/ (same paths, same [Authorize]/[AllowAnonymous] as the minimal APIs they replace).
app.MapControllers();

// React pages migrate under /app so the existing Blazor route table can continue serving every
// unmigrated page. Vite emits fingerprinted assets under wwwroot/app; client-side routes fall back to
// its entry document without broadening the fallback to API, MCP, auth, or Blazor URLs.
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

await app.RunAsync();

// Reads the principal's role (from either the cookie's ClaimTypes.Role or the JWT's "role" claim) and
// returns whether it meets the minimum. Backs the Member/Admin/Owner authorization policies.
// Deliberately enum-only: a custom role_definitions name does not parse, so custom-role members never
// match these coarse policies — their access comes exclusively from the per-permission policies, which
// resolve the role's grant set from role_definitions by name. The coarse ladder stays reserved for the
// four built-in roles.
static bool RoleAtLeast(ClaimsPrincipal user, UserRole min)
{
    var value = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
    return Enum.TryParse<UserRole>(value, out var role) && role >= min;
}
