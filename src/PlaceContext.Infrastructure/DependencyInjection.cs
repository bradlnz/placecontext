using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Git;
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
/// store, and wires every Application port to its adapter — the EF repositories, the git
/// adapters, and the skill scaffolder.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaceContextOptions>(configuration.GetSection("PlaceContext"));
        services.Configure<OpenSearch.OpenSearchOptions>(
            configuration.GetSection("PlaceContext:OpenSearch"));

        services.AddSingleton<IClock, SystemClock>();

        // (Activation/licensing removed — subscriptions are managed by a separate billing portal.)

        // Multi-tenancy: ambient current-tenant (AsyncLocal singleton) + the tenant registry.
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ICurrentProject, CurrentProject>();
        services.AddScoped<ITenantStore, EfTenantStore>();
        services.AddScoped<ITenantSettingsPort, EfTenantSettingsPort>();
        services.AddScoped<IMenuConfigService, Tenancy.MenuConfigService>();
        services.AddScoped<IArtifactViewConfigService, Tenancy.ArtifactViewConfigService>();
        services.AddScoped<IArtifactShareTokenService, Artifacts.ArtifactShareTokenService>();
        services.AddScoped<Crm.CrmIngestionSettingsService>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
        services.AddScoped<IMembershipService, Auth.MembershipService>();
        services.AddScoped<IUserApiTokenService, Auth.UserApiTokenService>();
        services.AddScoped<IOAuthClientStore, EfOAuthClientStore>();
        services.AddScoped<IOAuthRefreshTokenStore, EfOAuthRefreshTokenStore>();

        // Granular RBAC: ambient current-user (mirrors ICurrentTenant) + role-default/override
        // permission resolution + the tenant-scoped override store.
        services.AddSingleton<ICurrentUser, CurrentUser>();
        services.AddScoped<IUserPermissionGrantRepository, EfUserPermissionGrantRepository>();
        services.AddScoped<IRoleDefinitionRepository, EfRoleDefinitionRepository>();
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
        services.AddScoped<IJobTestStore, EfJobTestStore>();
        services.AddScoped<IOpenSearchDashboardStore, EfOpenSearchDashboardStore>();
        services.AddScoped<IOpenSearchConnectionResolver, OpenSearch.OpenSearchConnectionResolver>();
        services.AddScoped<IOpenSearchDataGateway, OpenSearch.OpenSearchDataGateway>();
        services.AddScoped<IOpenSearchSyncGateway, OpenSearch.OpenSearchSyncGateway>();
        services.AddHttpClient("opensearch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("opensearch-sync", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IActivityLogRepository, EfActivityLogRepository>();
        services.AddScoped<IDecisionRepository, EfDecisionRepository>();
        services.AddScoped<IRequirementsRepository, EfRequirementsRepository>();
        services.AddScoped<IUsageRepository, EfUsageRepository>();

        // Git, metrics, skill scaffolding.
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<ISkillScaffolder, FileSkillScaffolder>();
        services.AddSingleton<IRepoFiles, Files.FileRepoFiles>();
        services.AddHttpClient();
        services.AddSingleton<ICodeWorkspace, CodeWorkspace>();

        // Generic workload runner. In-cluster (the Host pod has KUBERNETES_SERVICE_HOST) we run jobs as
        // Kubernetes Jobs via the API + the Host's ServiceAccount/RBAC; otherwise (local dev) Docker.
        services.Configure<WorkloadRunnerOptions>(
            configuration.GetSection("PlaceContext:WorkloadRunner"));
        services.AddSingleton<IWorkloadOutputBuffer, InMemoryWorkloadOutputBuffer>();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            services.AddSingleton<IWorkloadRunner, KubernetesWorkloadRunner>();
        else
            services.AddSingleton<IWorkloadRunner, DockerWorkloadRunner>();

        // Field encryption at rest (AES via Data Protection). Portal/jobs decrypt in-process;
        // raw Postgres/MinIO without the DP key ring only see ciphertext.
        services.AddSingleton<IDataEncryptor, Security.DataProtectionEncryptor>();
        // Vault secrets: purpose-scoped façade over IDataEncryptor.
        services.AddScoped<IProjectSecretRepository, EfProjectSecretRepository>();
        services.AddSingleton<ISecretProtector, Security.DataProtectionSecretProtector>();

        // Object store (S3-compatible: MinIO, DO Spaces, AWS S3) for post-job artifacts.
        services.Configure<Storage.ObjectStoreOptions>(configuration.GetSection("PlaceContext:ObjectStore"));
        services.AddSingleton<IObjectStore, Storage.S3ObjectStore>();
        services.AddScoped<IRunArtifactLinkRepository, EfRunArtifactLinkRepository>();
        services.AddScoped<IProjectChartRepository, EfProjectChartRepository>();

        // Redis-backed distributed cache for job run shard results (keeps Postgres lean).
        var redisConn = configuration["PlaceContext:Redis:ConnectionString"];
        Type? innerChatMemoryStoreType = null;
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = redisConn;
                opts.InstanceName = "pc";
            });
            services.AddSingleton<Caching.IJobRunCache, Caching.RedisJobRunCache>();
            services.AddSingleton<Caching.RedisChatMemoryStore>();
            services.AddSingleton<Caching.IChatMemoryStore>(sp => sp.GetRequiredService<Caching.RedisChatMemoryStore>());
            innerChatMemoryStoreType = typeof(Caching.RedisChatMemoryStore);
        }
        else
        {
            services.AddSingleton<Caching.IJobRunCache, Caching.NullJobRunCache>();
            services.AddSingleton<Caching.NullChatMemoryStore>();
            services.AddSingleton<Caching.IChatMemoryStore>(sp => sp.GetRequiredService<Caching.NullChatMemoryStore>());
            innerChatMemoryStoreType = typeof(Caching.NullChatMemoryStore);
        }

        // Qdrant semantic search for cross-session chat memory (decorates Redis/Null store).
        var qdrantUrl = configuration["PlaceContext:Qdrant:Endpoint"];
        if (!string.IsNullOrWhiteSpace(qdrantUrl) && innerChatMemoryStoreType is not null)
        {
            var qdrantCollection = configuration["PlaceContext:Qdrant:Collection"] ?? "chat-memory";
            services.AddSingleton<Caching.IChatMemoryStore>(sp =>
            {
                var fallback = (Caching.IChatMemoryStore)sp.GetRequiredService(innerChatMemoryStoreType);
                var embeddings = sp.GetRequiredService<IEmbeddingGateway>();
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                return new Caching.QdrantChatMemoryStore(fallback, embeddings, http, qdrantUrl, qdrantCollection);
            });
        }

        // Launchpad agent sessions persist through the Application port onto the chat memory store.
        services.AddScoped<IAgentSessionStore, Caching.ChatMemoryAgentSessionStore>();

        // Slack Events API → agent → chat.postMessage (disabled when PlaceContext:Slack is incomplete).
        services.Configure<Slack.SlackOptions>(configuration.GetSection(Slack.SlackOptions.SectionName));
        var slackOpts = configuration.GetSection(Slack.SlackOptions.SectionName).Get<Slack.SlackOptions>() ?? new Slack.SlackOptions();
        if (slackOpts.IsConfigured)
            services.AddSingleton<ISlackClient, Slack.SlackApiClient>();
        else
            services.AddSingleton<ISlackClient, Slack.NullSlackClient>();
        if (!string.IsNullOrWhiteSpace(redisConn))
            services.AddSingleton<ISlackThreadSessionStore, Slack.DistributedCacheSlackThreadSessionStore>();
        else
            services.AddSingleton<ISlackThreadSessionStore, Slack.MemorySlackThreadSessionStore>();

        // Job / JobRun repositories.
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobRunRepository, EfJobRunRepository>();
        services.AddScoped<ICrmClientRepository, EfCrmClientRepository>();
        services.AddScoped<ICrmJobRunRepository, EfCrmJobRunRepository>();
        services.AddScoped<ICrmChainRunRepository, EfCrmChainRunRepository>();
        services.AddScoped<ICrmCommunicationRepository, EfCrmCommunicationRepository>();
        services.AddScoped<ICrmClientArtifactRepository, EfCrmClientArtifactRepository>();
        services.AddScoped<ICrmAutomationRuleRepository, EfCrmAutomationRuleRepository>();
        services.AddScoped<ICrmAutomationQueue, Scheduling.DbCrmAutomationQueue>();
        services.Configure<Comms.ClientCommsOptions>(
            configuration.GetSection(Comms.ClientCommsOptions.SectionName));
        services.AddScoped<Comms.CommunicationProviderService>();
        services.AddScoped<Comms.DatabaseCommunicationSender>();
        services.AddScoped<IClientCommunicationSender>(
            sp => sp.GetRequiredService<Comms.DatabaseCommunicationSender>());

        // Data map (declarative job-result → project-table ingestion rules).
        services.AddScoped<IDataMappingRepository, EfDataMappingRepository>();
        services.AddScoped<IDataEntityRepository, EfDataEntityRepository>();
        services.AddScoped<Application.Features.IEntityTagStore, EfEntityTagStore>();
        services.AddScoped<Application.Features.IRecordLinkStore, EfRecordLinkStore>();
        services.AddSingleton<IDocumentTextExtractor, Documents.DocumentTextExtractor>();

        // Trigger + event repositories.
        services.AddScoped<IJobTriggerRepository, EfJobTriggerRepository>();
        services.AddScoped<IJobChainRepository, EfJobChainRepository>();
        services.AddScoped<IChainRunRepository, EfChainRunRepository>();
        services.AddScoped<IEventRepository, EfEventRepository>();

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

        // Chat gateway: cluster (SafeTensors shard server) takes precedence,
        // then Ollama, else a no-op.
        var clusterEp = configuration["PlaceContext:ClusterChat:Endpoint"];
        var ollamaEp = configuration["PlaceContext:Chat:Endpoint"];
        if (!string.IsNullOrWhiteSpace(clusterEp))
        {
            services.AddSingleton<IChatGateway, Chat.ClusterChatGateway>();
            Console.WriteLine($"[di] IChatGateway -> ClusterChatGateway (endpoint={clusterEp})");
        }
        else if (!string.IsNullOrWhiteSpace(ollamaEp))
        {
            services.AddSingleton<IChatGateway, Chat.OllamaChatGateway>();
            Console.WriteLine($"[di] IChatGateway -> OllamaChatGateway (endpoint={ollamaEp})");
        }
        else
        {
            services.AddSingleton<IChatGateway, Chat.NullChatGateway>();
            Console.WriteLine("[di] IChatGateway -> NullChatGateway (no endpoint configured)");
        }

        // Agent repositories.
        services.AddScoped<IAgentConfigRepository, EfAgentConfigRepository>();
        services.AddScoped<IAgentChatSessionRepository, EfAgentChatSessionRepository>();
        services.AddScoped<Domain.Repositories.IMcpConnectionRepository, Persistence.EfMcpConnectionRepository>();
        services.AddScoped<IChatCommandRepository, EfChatCommandRepository>();

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
        services.AddHostedService<Scheduling.CrmAutomationWorker>();

        // Background portal operations (the notifications-pane ledger) + the analytics chart sweep
        // worker (generation can be slow; the portal only enqueues and reads stored charts).
        services.AddSingleton<Operations.OperationCenter>();
        services.AddSingleton<Scheduling.AnalyticsRefreshQueue>();
        services.AddHostedService<Scheduling.AnalyticsWorkerService>();

        // Run-status watcher: syncs persisted job/chain run statuses into the notifications pane on
        // a short tick, so the bell reflects finish/fail the moment the row commits — independent of
        // the (slow, best-effort) in-process enrichment and of which replica executed the run.
        services.AddScoped<IRunStatusReader, DbRunStatusReader>();
        services.AddSingleton<IRunStatusNotifier, Operations.OperationCenterRunStatusNotifier>();
        services.AddHostedService<Scheduling.RunStatusWatcherService>();

        // Each project's own database (Postgres schema + role isolation; Monaco SQL in the portal).
        services.AddScoped<IProjectDataStore, ProjectData.NpgsqlProjectDataStore>();

        // Cluster page: node inventory + fleet-master admin (promote / join codes over Tailscale).
        // Same in-cluster vs local detection as the workload runner above.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
        {
            services.AddSingleton<Cluster.KubernetesClusterInfoProvider>();
            services.AddSingleton<IClusterInfoProvider>(sp =>
                sp.GetRequiredService<Cluster.KubernetesClusterInfoProvider>());
            services.AddSingleton<IClusterAdminPort>(sp =>
                sp.GetRequiredService<Cluster.KubernetesClusterInfoProvider>());
        }
        else
        {
            services.AddSingleton<Cluster.LocalClusterInfoProvider>();
            services.AddSingleton<IClusterInfoProvider>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
            services.AddSingleton<IClusterAdminPort>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
        }

        // Mints fresh Tailscale auth keys from vaulted OAuth client credentials for agent join codes.
        services.AddSingleton<ITailscaleKeyMinter, Cluster.TailscaleApiKeyMinter>();

        // Agent join tokens — short-lived, one-time tokens exchanged for join codes from join.sh.
        services.AddSingleton<IAgentTokenManager, Cluster.InMemoryAgentTokenManager>();

        services.AddSingleton<Observability.JobTelemetryCollector>();
        services.AddSingleton<IJobTelemetryReader>(sp => sp.GetRequiredService<Observability.JobTelemetryCollector>());
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

        // Backfill denormalized shard counts for existing runs.
        // The JSON is encrypted at the app layer, so we must read/decrypt/count in C#.
        BackfillShardCounts(db, scope.ServiceProvider);
    }

    private static void BackfillShardCounts(AppDbContext db, IServiceProvider sp)
    {
        try
        {
            var enc = sp.GetService<IDataEncryptor>();
            if (enc is null) return;
            var purpose = IDataEncryptor.Purpose.JobRun;
            var rows = db.JobRuns
                .FromSqlRaw("""SELECT * FROM job_runs WHERE "ShardCount" = 0 AND "ShardResultsJson" IS NOT NULL""")
                .ToList();
            if (rows.Count == 0) return;

            foreach (var row in rows)
            {
                try
                {
                    var json = enc.Unprotect(row.ShardResultsJson, purpose);
                    var shards = System.Text.Json.JsonSerializer.Deserialize<List<ShardResultDto>>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (shards is null) continue;
                    row.ShardCount = shards.Count;
                    row.SucceededShards = shards.Count(s =>
                        string.Equals(s.Outcome, "Succeeded", StringComparison.OrdinalIgnoreCase));
                    row.PartialShards = shards.Count(s =>
                        string.Equals(s.Outcome, "Partial", StringComparison.OrdinalIgnoreCase));
                    row.FailedShards = shards.Count(s =>
                        string.Equals(s.Outcome, "Failed", StringComparison.OrdinalIgnoreCase));
                }
                catch { /* row with unparseable JSON — leave counts at 0 */ }
            }
            db.SaveChanges();
        }
        catch { /* backfill is best-effort */ }
    }

    private sealed class ShardResultDto
    {
        public string Outcome { get; set; } = "";
    }

    /// <summary>
    /// Encrypts any pre-existing plaintext at rest. Safe to call on every launch (idempotent).
    /// </summary>
    public static Task EncryptExistingDataAsync(IServiceProvider provider, CancellationToken ct = default)
        => Security.EncryptionAtRestBootstrap.RunAsync(provider, ct);

    /// <summary>
    /// Bounded, idempotent CRM-only backfill. Runs on every Host launch so customer data created by
    /// versions predating CRM field encryption cannot remain plaintext after an upgrade.
    /// </summary>
    public static Task EncryptExistingCrmDataAsync(IServiceProvider provider, CancellationToken ct = default)
        => Security.EncryptionAtRestBootstrap.RunCrmAsync(provider, ct);
}
