using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Git;
using PlaceContext.Infrastructure.Metrics;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Skills;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Workload;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: binds options, opens the EF Core (PostgreSQL)
/// store, and wires every Application port to its adapter — the EF repositories, the git/metrics
/// adapters, the skill scaffolder, and the Strategy-behind-Factory risk calculators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaceContextOptions>(configuration.GetSection("PlaceContext"));

        services.AddSingleton<IClock, SystemClock>();

        // (Activation/licensing removed — subscriptions are managed by a separate billing portal.)

        // Multi-tenancy: ambient current-tenant (AsyncLocal singleton) + the tenant registry.
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ICurrentProject, CurrentProject>();
        services.AddScoped<ITenantStore, EfTenantStore>();
        services.AddScoped<ITenantSettingsPort, EfTenantSettingsPort>();
        services.AddScoped<IMenuConfigService, Tenancy.MenuConfigService>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
        services.AddScoped<IMembershipService, Auth.MembershipService>();
        services.AddScoped<IUserApiTokenService, Auth.UserApiTokenService>();
        services.AddScoped<IOAuthClientStore, Persistence.EfOAuthClientStore>();
        services.AddScoped<IOAuthRefreshTokenStore, Persistence.EfOAuthRefreshTokenStore>();

        // Granular RBAC: ambient current-user (mirrors ICurrentTenant) + role-default/override
        // permission resolution + the tenant-scoped override store.
        services.AddSingleton<ICurrentUser, CurrentUser>();
        services.AddScoped<Domain.Repositories.IUserPermissionGrantRepository, Persistence.EfUserPermissionGrantRepository>();
        services.AddScoped<IPermissionService, Auth.PermissionService>();

        // EF Core code-first store. The DbContext is the request-scoped unit of work.
        var connectionString = configuration.GetSection("PlaceContext")["ConnectionString"]
            ?? new PlaceContextOptions().ConnectionString;
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Shared, persisted MCP tool-call log (singleton; opens short-lived scopes).
        services.AddSingleton<IToolCallLog, EfToolCallLog>();

        // EF repositories.
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IActivityLogRepository, EfActivityLogRepository>();
        services.AddScoped<IDecisionRepository, EfDecisionRepository>();
        services.AddScoped<IRiskAssessmentRepository, EfRiskAssessmentRepository>();
        // Knowledge context: encrypted file under DataRoot (not a DB column).
        services.AddScoped<IProjectContextRepository, Files.FileProjectContextRepository>();
        services.AddScoped<IRequirementsRepository, EfRequirementsRepository>();
        services.AddScoped<IUsageRepository, EfUsageRepository>();

        // Git, metrics, skill scaffolding.
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<ICodeMetricsProbe, FileScanCodeMetricsProbe>();
        services.AddSingleton<ISkillScaffolder, FileSkillScaffolder>();
        services.AddSingleton<IRepoFiles, Files.FileRepoFiles>();
        services.AddHttpClient();
        services.AddSingleton<ICodeWorkspace, Git.CodeWorkspace>();

        // Generic workload runner. In-cluster (the Host pod has KUBERNETES_SERVICE_HOST) we run jobs as
        // Kubernetes Jobs via the API + the Host's ServiceAccount/RBAC; otherwise (local dev) Docker.
        services.Configure<WorkloadRunnerOptions>(
            configuration.GetSection("PlaceContext:WorkloadRunner"));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            services.AddSingleton<IWorkloadRunner, Workload.KubernetesWorkloadRunner>();
        else
            services.AddSingleton<IWorkloadRunner, DockerWorkloadRunner>();

        // Field encryption at rest (AES via Data Protection). Portal/jobs decrypt in-process;
        // raw Postgres/MinIO without the DP key ring only see ciphertext.
        services.AddSingleton<Application.Ports.IDataEncryptor, Security.DataProtectionEncryptor>();
        // Vault secrets: purpose-scoped façade over IDataEncryptor.
        services.AddScoped<Domain.Repositories.IProjectSecretRepository, Persistence.EfProjectSecretRepository>();
        services.AddSingleton<Application.Ports.ISecretProtector, Security.DataProtectionSecretProtector>();

        // Object store (MinIO, S3-compatible) for post-job artifacts: HTML reports, charts, CSVs, bundles.
        services.Configure<Storage.ObjectStoreOptions>(configuration.GetSection("PlaceContext:ObjectStore"));
        services.AddSingleton<Application.Ports.IObjectStore, Storage.MinioObjectStore>();
        services.AddScoped<Domain.Repositories.IRunArtifactLinkRepository, Persistence.EfRunArtifactLinkRepository>();
        services.AddScoped<Domain.Repositories.IProjectChartRepository, Persistence.EfProjectChartRepository>();

        // Job / JobRun repositories.
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobRunRepository, EfJobRunRepository>();

        // Data map (declarative job-result → project-table ingestion rules).
        services.AddScoped<IDataMappingRepository, EfDataMappingRepository>();
        services.AddScoped<IDataEntityRepository, EfDataEntityRepository>();
        services.AddScoped<Application.Features.IEntityTagStore, Persistence.EfEntityTagStore>();
        services.AddSingleton<IDocumentTextExtractor, Documents.PdfPigTextExtractor>();

        // Trigger + event repositories.
        services.AddScoped<IJobTriggerRepository, EfJobTriggerRepository>();
        services.AddScoped<IJobChainRepository, EfJobChainRepository>();
        services.AddScoped<IChainRunRepository, EfChainRunRepository>();
        services.AddScoped<IEventRepository, EfEventRepository>();

        // Embeddings: Voyage AI when a key is configured, else a no-op.
        // The pgvector-backed run-embedding store self-initializes lazily and degrades if unavailable.
        if (!string.IsNullOrWhiteSpace(configuration["PlaceContext:Voyage:ApiKey"]))
            services.AddSingleton<IEmbeddingGateway, Embeddings.VoyageEmbeddingGateway>();
        else
            services.AddSingleton<IEmbeddingGateway, Embeddings.NullEmbeddingGateway>();
        services.AddScoped<IRunEmbeddingRepository, EfRunEmbeddingRepository>();
        // Universal RAG: any content kind, encrypted source text + pgvector.
        services.AddScoped<IContentIndexer, Embeddings.ContentIndexer>();

        // Dependency-graph assembly is expensive (full ledger + decisions + O(n²) embedding weave);
        // wrap the Application provider in a short-TTL cache so page opens and brain rollups don't
        // recompute it every time. Registered after AddApplication, so this mapping wins resolution.
        services.AddMemoryCache();
        services.AddScoped<Application.Features.DecisionTreeProvider>();
        services.AddScoped<Application.Features.IDecisionTreeProvider, Caching.CachedDecisionTreeProvider>();

        // Trigger scheduling: cron evaluation, a durable DB-backed run queue, and the background firing
        // service (advisory-lock-elected schedule scan + atomic queue drain — correct across replicas).
        services.AddSingleton<ICronSchedule, Scheduling.CronosCronSchedule>();
        services.AddScoped<IJobRunQueue, Scheduling.DbJobRunQueue>();
        services.AddHostedService<Scheduling.TriggerSchedulerService>();

        // Background portal operations (the notifications-pane ledger) + the analytics chart sweep
        // worker (generation can be slow; the portal only enqueues and reads stored charts).
        services.AddSingleton<Operations.OperationCenter>();
        services.AddSingleton<Scheduling.AnalyticsRefreshQueue>();
        services.AddHostedService<Scheduling.AnalyticsWorkerService>();

        // Run-status watcher: syncs persisted job/chain run statuses into the notifications pane on
        // a short tick, so the bell reflects finish/fail the moment the row commits — independent of
        // the (slow, best-effort) in-process enrichment and of which replica executed the run.
        services.AddScoped<IRunStatusReader, Persistence.DbRunStatusReader>();
        services.AddSingleton<IRunStatusNotifier, Operations.OperationCenterRunStatusNotifier>();
        services.AddHostedService<Scheduling.RunStatusWatcherService>();

        // Each project's own database (Postgres schema + role isolation; Monaco SQL in the portal).
        services.AddScoped<IProjectDataStore, ProjectData.NpgsqlProjectDataStore>();

        // Cluster page: node inventory + fleet-master admin (promote / join codes over Tailscale).
        // Same in-cluster vs local detection as the workload runner above.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
        {
            services.AddSingleton<Cluster.KubernetesClusterInfoProvider>();
            services.AddSingleton<Application.Ports.IClusterInfoProvider>(sp =>
                sp.GetRequiredService<Cluster.KubernetesClusterInfoProvider>());
            services.AddSingleton<Application.Ports.IClusterAdminPort>(sp =>
                sp.GetRequiredService<Cluster.KubernetesClusterInfoProvider>());
        }
        else
        {
            services.AddSingleton<Cluster.LocalClusterInfoProvider>();
            services.AddSingleton<Application.Ports.IClusterInfoProvider>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
            services.AddSingleton<Application.Ports.IClusterAdminPort>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
        }

        // Mints fresh Tailscale auth keys from vaulted OAuth client credentials for agent join codes.
        services.AddSingleton<Application.Ports.ITailscaleKeyMinter, Cluster.TailscaleApiKeyMinter>();

        services.AddSingleton<Observability.JobTelemetryCollector>();
        services.AddSingleton<Application.Ports.IJobTelemetryReader>(sp => sp.GetRequiredService<Observability.JobTelemetryCollector>());
        services.AddHostedService<Observability.JobTelemetryCollectorStartup>();

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
        // Additive column for menu customization (safe if already present).
        try
        {
            db.Database.ExecuteSqlRaw(
                """ALTER TABLE tenants ADD COLUMN IF NOT EXISTS "MenuJson" text NULL""");
        }
        catch { /* non-Postgres or already applied via migration */ }

        // Additive indexes for the hot run queries (safe if already present). The status watcher
        // scans job_runs/chain_runs for in-flight or recently-finished rows every 2 seconds — a
        // sequential scan of the whole run history without these. Partial indexes keep the active-
        // status side tiny (the working set of live runs), FinishedAt serves the "recently finished"
        // arm of the OR, and (TenantId, StartedAt) serves the tenant-filtered newest-first lists.
        try
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE INDEX IF NOT EXISTS ix_job_runs_active ON job_runs ("Status") WHERE "Status" IN ('Queued','Running');
                CREATE INDEX IF NOT EXISTS ix_job_runs_finished_at ON job_runs ("FinishedAt");
                CREATE INDEX IF NOT EXISTS ix_job_runs_tenant_started ON job_runs ("TenantId", "StartedAt");
                CREATE INDEX IF NOT EXISTS ix_chain_runs_active ON chain_runs ("Status") WHERE "Status" IN ('Queued','Running');
                CREATE INDEX IF NOT EXISTS ix_chain_runs_finished_at ON chain_runs ("FinishedAt");
                CREATE INDEX IF NOT EXISTS ix_chain_runs_tenant_started ON chain_runs ("TenantId", "StartedAt");
                """);
        }
        catch { /* non-Postgres, or the tables predate these columns */ }
    }

    /// <summary>
    /// Encrypts any pre-existing plaintext at rest and migrates knowledge context off the DB onto
    /// encrypted files. Safe to call on every launch (idempotent).
    /// </summary>
    public static Task EncryptExistingDataAsync(IServiceProvider provider, CancellationToken ct = default)
        => Security.EncryptionAtRestBootstrap.RunAsync(provider, ct);
}
