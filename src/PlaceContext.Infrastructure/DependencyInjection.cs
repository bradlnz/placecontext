using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Git;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.CustomerPortal;
using PlaceContext.Infrastructure.Skills;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PlaceContext.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: binds options, opens the EF Core (PostgreSQL)
/// store, and wires every Application port to its adapter — the EF repositories, the git
/// adapters, and the skill scaffolder.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlaceContextOptions>(configuration.GetSection("PlaceContext"));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<CurrentTenant>();
        services.AddSingleton<ICurrentTenant>(provider => provider.GetRequiredService<CurrentTenant>());
        services.AddSingleton<ICurrentTenantAccessor>(provider => provider.GetRequiredService<CurrentTenant>());
        services.AddScoped<ITenantCatalog, EfTenantCatalog>();
        services.AddScoped<ITenantStore, EfTenantStore>();
        services.AddScoped<IRequestTenantResolver, LegacyRequestTenantResolver>();
        services.AddSingleton<ICurrentProject, CurrentProject>();
        services.AddSingleton<CurrentUser>();
        services.AddSingleton<ICurrentUser>(provider => provider.GetRequiredService<CurrentUser>());
        services.AddSingleton<ICurrentUserAccessor>(provider => provider.GetRequiredService<CurrentUser>());

        var connectionString = configuration.GetSection("PlaceContext")["ConnectionString"]
            ?? new PlaceContextOptions().ConnectionString;
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            o.ConfigureWarnings(w =>
            {
                w.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddSingleton<IDataEncryptor, Security.DataProtectionEncryptor>();
        services.AddHttpClient();
        services.AddSingleton<Operations.OperationCenter>();
        services.AddSingleton<IBackgroundOperationNotifier,
            Operations.OperationCenterBackgroundOperationNotifier>();
        services.AddSingleton<IRunStatusNotifier, Operations.OperationCenterRunStatusNotifier>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructureCore(configuration);

        // (Activation/licensing removed — subscriptions are managed by a separate billing portal.)

        // Multi-tenancy: ambient current-tenant (AsyncLocal singleton) + the tenant registry.
        services.AddScoped<ITenantStore, EfTenantStore>();
        services.AddScoped<ITenantSettingsPort, EfTenantSettingsPort>();
        services.AddScoped<IMenuConfigService, Tenancy.MenuConfigService>();
        services.AddScoped<IArtifactViewConfigService, Tenancy.ArtifactViewConfigService>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
        services.AddScoped<IMembershipService, Auth.MembershipService>();
        services.AddScoped<IUserApiTokenService, Auth.UserApiTokenService>();
        services.AddScoped<IOAuthClientStore, EfOAuthClientStore>();
        services.AddScoped<IOAuthRefreshTokenStore, EfOAuthRefreshTokenStore>();

        // Granular RBAC: ambient current-user (mirrors ICurrentTenant) + role-default/override
        // permission resolution + the tenant-scoped override store.
        services.AddScoped<IUserPermissionGrantRepository, EfUserPermissionGrantRepository>();
        services.AddScoped<IRoleDefinitionRepository, EfRoleDefinitionRepository>();
        services.AddScoped<IPermissionService, Auth.PermissionService>();

        // Shared, persisted MCP tool-call log (singleton; opens short-lived scopes).
        services.AddSingleton<IToolCallLog, EfToolCallLog>();

        // EF repositories.
        services.AddScoped<IActivityLogRepository, EfActivityLogRepository>();
        services.AddScoped<IDecisionRepository, EfDecisionRepository>();
        services.AddScoped<IRequirementsRepository, EfRequirementsRepository>();

        // Git, metrics, skill scaffolding.
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<ISkillScaffolder, FileSkillScaffolder>();
        services.AddSingleton<IRepoFiles, Files.FileRepoFiles>();
        services.AddSingleton<ICodeWorkspace, CodeWorkspace>();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            services.AddSingleton<ICustomerPortalProvisioner, CustomerPortalProvisioningService>();
        else
            services.AddSingleton<ICustomerPortalProvisioner, NoOpCustomerPortalProvisioner>();

        // Field encryption at rest (AES via Data Protection). Portal/jobs decrypt in-process;
        // raw Postgres/MinIO without the DP key ring only see ciphertext.
        // Object store (S3-compatible: MinIO, DO Spaces, AWS S3) for post-job artifacts.


        // Job / JobRun repositories.
        services.Configure<Comms.ClientCommsOptions>(
            configuration.GetSection(Comms.ClientCommsOptions.SectionName));
        services.AddScoped<Comms.CommunicationProviderService>();
        services.AddScoped<Comms.DatabaseCommunicationSender>();
        services.AddScoped<IClientCommunicationSender>(
            sp => sp.GetRequiredService<Comms.DatabaseCommunicationSender>());

        // Data map (declarative job-result → project-table ingestion rules).

        // Trigger + event repositories.

        // Embeddings: Voyage AI when a key is configured, else the cluster shard server
        // (self-hosted, vectors from the chat model's hidden states), else a no-op.
        if (!string.IsNullOrWhiteSpace(configuration["PlaceContext:Voyage:ApiKey"]))
            services.AddSingleton<IEmbeddingGateway, Embeddings.VoyageEmbeddingGateway>();
        else if (!string.IsNullOrWhiteSpace(configuration["PlaceContext:ClusterChat:Endpoint"]))
            services.AddSingleton<IEmbeddingGateway, Embeddings.ClusterEmbeddingGateway>();
        else
            services.AddSingleton<IEmbeddingGateway, Embeddings.NullEmbeddingGateway>();

        // Vector stores: Qdrant when an endpoint is configured, otherwise pgvector (with lazy init).
        var qdrantUrl2 = configuration["PlaceContext:Qdrant:Endpoint"];
        if (!string.IsNullOrWhiteSpace(qdrantUrl2))
        {
            services.AddScoped<IRunEmbeddingRepository>(sp =>
            {
                var embeddings = sp.GetRequiredService<IEmbeddingGateway>();
                var tenant = sp.GetRequiredService<ICurrentTenant>();
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                return new VectorStore.QdrantRunEmbeddingRepository(embeddings, tenant, httpFactory, qdrantUrl2);
            });
            services.AddScoped<IContentIndexer>(sp =>
            {
                var gateway = sp.GetRequiredService<IEmbeddingGateway>();
                var tenant = sp.GetRequiredService<ICurrentTenant>();
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                return new VectorStore.QdrantContentIndexer(gateway, tenant, httpFactory, qdrantUrl2);
            });
            // One-shot migration from pgvector tables on first startup.
            services.AddHostedService<VectorStore.MigrateToQdrantService>();
        }
        else
        {
            services.AddScoped<IRunEmbeddingRepository, Persistence.EfRunEmbeddingRepository>();
            services.AddScoped<IContentIndexer, Embeddings.ContentIndexer>();
        }


        // Dependency-graph assembly is expensive (full ledger + decisions + O(n²) embedding weave);
        // wrap the Application provider in a short-TTL cache so page opens and brain rollups don't
        // recompute it every time. Registered after AddApplication, so this mapping wins resolution.
        services.AddMemoryCache();
        services.AddScoped<Application.Features.IDecisionTreeProvider, Caching.CachedDecisionTreeProvider>();
        services.AddHostedService<Caching.DecisionTreeCacheWarmer>();

        // Trigger scheduling: cron evaluation, a durable DB-backed run queue, and the background firing
        // service (advisory-lock-elected schedule scan + atomic queue drain — correct across replicas).

        // Background portal operations (the notifications-pane ledger) + the analytics chart sweep
        // worker (generation can be slow; the portal only enqueues and reads stored charts).
        services.AddSingleton<Scheduling.AnalyticsRefreshQueue>();
        services.AddHostedService<Scheduling.AnalyticsWorkerService>();

        // Run-status watcher: syncs persisted job/chain run statuses into the notifications pane on
        // a short tick, so the bell reflects finish/fail the moment the row commits — independent of
        // the (slow, best-effort) in-process enrichment and of which replica executed the run.

        // Each project's own database (Postgres schema + role isolation; Monaco SQL in the portal).
        // A project's external database override resolves through the Vault (cluster DB is the default).

        // Agent/fleet cluster adapters are composed by PlaceContext.Agents.Infrastructure.


        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations at startup (creating the schema on a fresh database).
    /// Migrations replace the old <c>EnsureCreated</c> so schema changes apply non-destructively — no
    /// more dropping the database. Call once from the composition root.
    /// </summary>
    public static void MigrateDatabase(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        // Additive columns for workspace UI customization (safe if already present).
        try
        {
            db.Database.ExecuteSqlRaw(
                """
                ALTER TABLE tenants ADD COLUMN IF NOT EXISTS "MenuJson" text NULL;
                ALTER TABLE tenants ADD COLUMN IF NOT EXISTS "ArtifactViewJson" text NULL;
                """);
        }
        catch { /* non-Postgres or already applied via migration */ }

    }

    /// <summary>
    /// Encrypts any pre-existing plaintext at rest. Safe to call on every launch (idempotent).
    /// </summary>
    public static Task EncryptExistingDataAsync(IServiceProvider provider, CancellationToken ct = default)
        => Security.EncryptionAtRestBootstrap.RunAsync(provider, ct);

}
