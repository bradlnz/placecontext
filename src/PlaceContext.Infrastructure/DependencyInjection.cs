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
        services.AddScoped<ITenantStore, EfTenantStore>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
        services.AddScoped<IMembershipService, Auth.MembershipService>();
        services.AddScoped<IOAuthClientStore, Persistence.EfOAuthClientStore>();
        services.AddScoped<IOAuthRefreshTokenStore, Persistence.EfOAuthRefreshTokenStore>();

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
        services.AddScoped<IProjectContextRepository, EfProjectContextRepository>();
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

        // Vault: encrypted project secrets (AES at rest via Data Protection) injected into job runs.
        services.AddScoped<Domain.Repositories.IProjectSecretRepository, Persistence.EfProjectSecretRepository>();
        services.AddSingleton<Application.Ports.ISecretProtector, Security.DataProtectionSecretProtector>();

        // Object store (MinIO, S3-compatible) for post-job artifacts: HTML reports, charts, CSVs, bundles.
        services.Configure<Storage.ObjectStoreOptions>(configuration.GetSection("PlaceContext:ObjectStore"));
        services.AddSingleton<Application.Ports.IObjectStore, Storage.MinioObjectStore>();
        services.AddScoped<Domain.Repositories.IRunArtifactLinkRepository, Persistence.EfRunArtifactLinkRepository>();
        services.AddScoped<Domain.Repositories.IProjectChartRepository, Persistence.EfProjectChartRepository>();
        services.AddScoped<Domain.Repositories.IInboundSmsRepository, Persistence.EfInboundSmsRepository>();

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
    }
}
