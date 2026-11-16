using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Activity;
using PlaceContext.Application.Artifacts;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Communications;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Networking;
using PlaceContext.Application.Observability;
using PlaceContext.Application.Organization;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Projects;
using PlaceContext.Application.SavedQueries;
using PlaceContext.Application.Search;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Skills;
using PlaceContext.Application.Tenancy;
using PlaceContext.Application.Triggers;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Application.Workload;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Git;
using PlaceContext.Infrastructure.Mcp;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Skills;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.Application.Access;

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
        services.Configure<Billing.PaymentBillingOptions>(
            configuration.GetSection("PlaceContext:Stripe"));
        services.AddOptions<PlaceContext.Application.Billing.RunBillingDeploymentOptions>()
            .Bind(configuration.GetSection("PlaceContext:SaaS"))
            .Validate(x => !x.ManagedHosting || x.BasicRunsPerPeriod > 0 && x.StandardRunsPerPeriod > 0
                && x.BasicAiRequestsPerPeriod > 0 && x.StandardAiRequestsPerPeriod > 0,
                "Cloud run and AI request limits must be positive.").ValidateOnStart();
        services.Configure<PlaceContext.Application.Billing.WorkspaceSignupOptions>(
            configuration.GetSection("PlaceContext:SaaS:Signup"));
        services.AddScoped<Auth.ManagedWorkspaceSignup>();
        services.AddScoped<PlaceContext.Application.Auth.IWorkspaceSignup>(sp => sp.GetRequiredService<Auth.ManagedWorkspaceSignup>());
        services.AddScoped<PlaceContext.Application.Billing.IWorkspaceSignupEmailSender,
            Auth.TransactionalSignupEmailSender>();
        services.AddHttpClient<IRemoteOAuthClientResolver, Auth.RemoteOAuthClientResolver>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddHttpClient("signup-mailer", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.Configure<Connections.ManagedOfficeOAuthOptions>(
            configuration.GetSection("PlaceContext:ManagedOAuth:Microsoft"));
        services.AddScoped<Billing.DbRunAllowance>();
        services.AddScoped<PlaceContext.Application.Billing.IRunAllowance>(sp => sp.GetRequiredService<Billing.DbRunAllowance>());
        services.AddTransient<Billing.ManagedAiUsageHandler>();
        services.AddScoped<PlaceContext.Application.Billing.IExecutionCapacity, Billing.DbExecutionCapacity>();
        services.AddScoped<PlaceContext.Application.Billing.IRunBillingPortal, Billing.PaymentBillingPortal>();
        services.AddScoped<PlaceContext.Application.Billing.IRunBillingWebhook, Billing.PaymentBillingWebhook>();
        services.AddScoped<PlaceContext.Application.Connections.IManagedOAuthConnections, Connections.ManagedOfficeOAuthConnections>();
        services.AddHttpClient("stripe-billing", client => client.Timeout = TimeSpan.FromSeconds(25))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("managed-microsoft-oauth", client => client.Timeout = TimeSpan.FromSeconds(25))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.Configure<Search.SearchOptions>(
            configuration.GetSection("PlaceContext:OpenSearch"));

        services.AddScoped<PlaceContext.Application.Build.IBuildChatStore, Build.DbBuildChatStore>();
        services.AddScoped<PlaceContext.Application.Build.IProjectAgentStore, Build.DbProjectAgentStore>();
        services.AddScoped<PlaceContext.Application.Build.IWorkflowDemoShares, Build.WorkflowDemoShares>();
        services.AddScoped<PlaceContext.Application.Build.IConversationShares, Build.ConversationShares>();
        services.AddScoped<Build.LocalCliModels>();
        services.AddScoped<Build.ModelConnections>();
        services.AddScoped<PlaceContext.Application.Build.IModelConnections>(sp => sp.GetRequiredService<Build.ModelConnections>());
        services.AddScoped<PlaceContext.Application.AgentWorkflows.IAgentModelGateway>(sp => sp.GetRequiredService<Build.ModelConnections>());
        services.Configure<AgentWorkflows.AgentWorkflowOptions>(configuration.GetSection("PlaceContext:AgentWorkflows"));
        services.AddScoped<PlaceContext.Application.AgentWorkflows.IAgentWorkflowStore, AgentWorkflows.EfAgentWorkflowStore>();
        services.AddSingleton(sp => new PlaceContext.Application.AgentWorkflows.AgentWorkflowPolicy(() =>
            sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName == "Development"
            && sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentWorkflows.AgentWorkflowOptions>>().Value.SimulateSubscriptions));
        services.AddScoped<IBuildModelGateway>(sp => sp.GetRequiredService<Build.ModelConnections>());
        services.AddHttpClient("build-model", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<Billing.ManagedAiUsageHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddScoped<IMcpClientService, McpClientService>();
        services.AddScoped<IMcpOAuthClient, HttpMcpOAuthClient>();
        services.AddScoped<IMcpConnectionProbe, HttpMcpConnectionProbe>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddHostedService<Build.BuildChatBackgroundService>();
        services.AddScoped<IWorkspaceAdministratorReader, Auth.EfWorkspaceAdministratorReader>();
        services.AddScoped<Application.DatabaseInspection.IDatabaseInspector, DatabaseInspector>();
        services.AddScoped<IExternalIdentityProvider, Auth.HttpExternalIdentityProvider>();
        services.AddScoped<IPresignedUploadClient, Storage.HttpPresignedUploadClient>();

        // (Activation/licensing removed — subscriptions are managed by a separate billing portal.)

        // Multi-tenancy: ambient current-tenant (AsyncLocal singleton) + the tenant registry.
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<ICurrentProject, CurrentProject>();
        services.AddScoped<ITenantStore, EfTenantStore>();
        services.AddSingleton<ClusterTenantRuntime>();
        services.AddSingleton<ITenantRuntime>(sp => sp.GetRequiredService<ClusterTenantRuntime>());
        services.AddScoped<ICloudWorkspace, CloudWorkspace>();
        services.AddHostedService<CloudWorkspaceProvisioner>();
        services.AddScoped<ITenantSettingsPort, EfTenantSettingsPort>();
        services.AddScoped<ITenantBrandingStore, EfTenantBrandingStore>();
        services.AddScoped<IMenuConfigService, Tenancy.MenuConfigService>();
        services.AddScoped<IArtifactViewConfigService, Tenancy.ArtifactViewConfigService>();
        services.AddScoped<IWorkerNetworkService, Networking.DbWorkerNetworkService>();
        services.AddScoped<IArtifactShareTokenService, Artifacts.ArtifactShareTokenService>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
        services.AddScoped<IMembershipService, Auth.MembershipService>();
        services.AddScoped<IProjectAssignments, Auth.ProjectAssignments>();
        services.AddScoped<IUserApiTokenService, Auth.UserApiTokenService>();
        services.AddScoped<IJobDataTokenService, Auth.JobDataTokenService>();
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
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            o.ConfigureWarnings(w =>
            {
                w.Ignore(RelationalEventId.PendingModelChangesWarning);
            });
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Shared, persisted MCP tool-call log (singleton; opens short-lived scopes).
        services.AddSingleton<IToolCallLog, EfToolCallLog>();
        services.AddScoped<PlaceContext.Application.DataSources.IDataSourceStore, DataSources.EfDataSourceStore>();
        services.AddScoped<PlaceContext.Application.DataSources.ISourceQueueDelivery, DataSources.SourceQueueDelivery>();
        services.AddScoped<DataSources.DatabaseSourceReader>();
        services.AddScoped<DataSources.ConnectedSourceReader>();
        services.AddScoped<PlaceContext.Application.DataSources.IDataSourceReader>(sp => sp.GetRequiredService<DataSources.ConnectedSourceReader>());
        services.AddScoped<PlaceContext.Application.DataSources.IDataSourceDiscovery>(sp => sp.GetRequiredService<DataSources.ConnectedSourceReader>());
        services.AddScoped<PlaceContext.Application.DataSources.IDataFeedAccess>(sp => sp.GetRequiredService<DataSources.ConnectedSourceReader>());
        services.AddHttpClient("data-source", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.MaxResponseContentBufferSize = 8_000_000;
        })
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        // EF repositories.
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IJobTestStore, EfJobTestStore>();
        services.AddScoped<ISearchDashboardStore, EfSearchDashboardStore>();
        services.AddScoped<ISavedQueryStore, EfSavedQueryStore>();
        services.AddScoped<ISearchConnectionResolver, Search.SearchConnectionResolver>();
        services.AddScoped<ISearchDataGateway, Search.SearchDataGateway>();
        services.AddScoped<ISearchSyncGateway, Search.SearchSyncGateway>();
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
        services.AddSingleton<ICodeWorkspace, CodeWorkspace>();

        // Workloads use Kubernetes in-cluster or the explicitly configured local kubeconfig.
        services.Configure<WorkloadRunnerOptions>(
            configuration.GetSection("PlaceContext:WorkloadRunner"));
        services.AddOptions<VirtualMachineEnvironmentOptions>().Bind(configuration.GetSection("PlaceContext:Qemu"))
            .Validate(x => !configuration.GetValue<bool>("PlaceContext:SaaS:ManagedHosting") || x.Agent.Provider != "codex",
                "Cloud agents must use a metered HTTP model provider, not a host CLI account.").ValidateOnStart();
        services.AddHttpClient("qemu-agent", client => client.Timeout = TimeSpan.FromSeconds(90))
            .AddHttpMessageHandler<Billing.ManagedAiUsageHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<VirtualMachinePromptAgent>();
        services.AddSingleton<ReviewedVirtualMachineTaskAgent>();
        services.AddSingleton<IVirtualMachineTaskAgent>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VirtualMachineEnvironmentOptions>>().Value.Executor == "model"
                ? sp.GetRequiredService<VirtualMachinePromptAgent>() : sp.GetRequiredService<ReviewedVirtualMachineTaskAgent>());
        services.AddSingleton<IChainQuestionGenerator, ModelChainQuestionGenerator>();
        services.Configure<ModelReviewOptions>(configuration.GetSection("PlaceContext:TypeSafe"));
        services.PostConfigure<ModelReviewOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = configuration["TYPESAFE_API_KEY"] ?? "";
            }
        });
        services.AddSingleton<ITypeSafeRateBudget, TypeSafeRateBudget>();
        services.AddTransient<TypeSafeRateLimitHandler>();
        services.AddHttpClient("typesafe", client => client.Timeout = TimeSpan.FromSeconds(90))
            .AddHttpMessageHandler<TypeSafeRateLimitHandler>()
            .AddHttpMessageHandler<Billing.ManagedAiUsageHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<IModelCredentialsResolver, ModelCredentialsResolver>();
        services.AddSingleton<IOutcomeReviewer, ModelOutcomeReviewer>();
        services.AddScoped<IVirtualMachineReplayStore, DbVirtualMachineReplayStore>();
        services.AddSingleton<VirtualMachineDesktopSessions>();
        services.AddScoped<IVirtualMachineDesktop, VirtualMachineDesktopAccess>();
        services.AddScoped<IVirtualMachineLoginStore, DbVirtualMachineLoginStore>();
        services.AddSingleton<VirtualMachineEnvironmentRunner>();
        services.AddSingleton<IVirtualMachineEnvironmentRunner>(sp => sp.GetRequiredService<VirtualMachineEnvironmentRunner>());
        services.AddSingleton<IVirtualMachineEnvironmentCatalog>(sp => sp.GetRequiredService<VirtualMachineEnvironmentRunner>());
        services.AddSingleton<IWorkloadOutputBuffer, InMemoryWorkloadOutputBuffer>();
        services.Configure<ContainerWorkflowOptions>(configuration.GetSection("PlaceContext:ContainerWorkflows"));
        services.AddSingleton<ContainerWorkflowBackgroundService>();
        services.AddSingleton<IContainerWorkflowRunner>(sp => sp.GetRequiredService<ContainerWorkflowBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<ContainerWorkflowBackgroundService>());
        services.AddSingleton<ClusterWorkloadRunner>();
        services.AddSingleton<k8s.IKubernetes>(sp => new k8s.Kubernetes(ClusterRuntime.Configuration(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkloadRunnerOptions>>().Value)));
        services.AddScoped<ClusterServiceConsole>();
        services.AddScoped<IServiceConsole>(sp => sp.GetRequiredService<ClusterServiceConsole>());
        services.AddScoped<IDatabaseConsole>(sp => sp.GetRequiredService<ClusterServiceConsole>());
        services.AddHttpClient("persistent-workloads", client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IWorkloadRunner>(sp => new ReviewedWorkloadRunner(
            sp.GetRequiredService<ClusterWorkloadRunner>(), sp.GetRequiredService<IOutcomeReviewer>()));

        // Field encryption at rest (AES via Data Protection). Portal/jobs decrypt in-process;
        // raw Postgres/MinIO without the DP key ring only see ciphertext.
        services.AddSingleton<IDataEncryptor, Security.DataProtectionEncryptor>();
        // Vault secrets: purpose-scoped façade over IDataEncryptor.
        services.AddScoped<IProjectSecretRepository, EfProjectSecretRepository>();
        services.AddSingleton<ISecretProtector, Security.DataProtectionSecretProtector>();

        // Object store (S3-compatible: MinIO, DO Spaces, AWS S3) for post-job artifacts.
        services.Configure<Storage.ObjectStoreOptions>(configuration.GetSection("PlaceContext:ObjectStore"));
        services.AddSingleton<IObjectStore, Storage.CompatibleObjectStore>();
        services.AddScoped<IRunArtifactLinkRepository, EfRunArtifactLinkRepository>();
        services.AddScoped<IProjectChartRepository, EfProjectChartRepository>();

        // Redis-backed distributed cache for job run shard results (keeps Postgres lean).
        var redisConn = configuration["PlaceContext:Redis:ConnectionString"];
        void ConfigureRunCache()
        {
            if (!string.IsNullOrWhiteSpace(redisConn))
            {
                services.AddStackExchangeRedisCache(opts =>
                {
                    opts.Configuration = redisConn;
                    opts.InstanceName = "pc";
                });
                services.AddSingleton<Caching.IJobRunCache, Caching.DistributedJobRunCache>();
                services.AddSingleton<IChainContextStore, Caching.DistributedChainContextStore>();
                return;
            }
            services.AddSingleton<Caching.IJobRunCache, Caching.NullJobRunCache>();
            services.AddSingleton<IChainContextStore, Caching.NullChainContextStore>();
        }
        ConfigureRunCache();
        // Job / JobRun repositories.
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobRunRepository, EfJobRunRepository>();
        services.AddScoped<IJobRunHeartbeat, Scheduling.DbJobRunHeartbeat>();
        services.Configure<Comms.ClientCommsOptions>(
            configuration.GetSection(Comms.ClientCommsOptions.SectionName));
        services.AddScoped<PlaceContext.Application.Communications.ICommunicationProviderStore, Comms.EfCommunicationProviderStore>();
        services.AddScoped<Comms.DatabaseCommunicationSender>();
        services.AddScoped<ICommunicationTestSender>(sp => sp.GetRequiredService<Comms.DatabaseCommunicationSender>());
        services.AddScoped<IClientCommunicationSender>(
            sp => sp.GetRequiredService<Comms.DatabaseCommunicationSender>());

        // Data map (declarative job-result → project-table ingestion rules).
        services.AddScoped<IDataMappingRepository, EfDataMappingRepository>();
        services.AddScoped<IDataEntityRepository, EfDataEntityRepository>();
        services.AddScoped<Application.DataEntities.IEntityTagStore, EfEntityTagStore>();
        services.AddScoped<Application.DataLinks.IRecordLinkStore, EfRecordLinkStore>();
        services.AddSingleton<IDocumentTextExtractor, Documents.DocumentTextExtractor>();

        // Trigger + event repositories.
        services.AddScoped<IJobTriggerRepository, EfJobTriggerRepository>();
        services.AddScoped<IJobChainRepository, EfJobChainRepository>();
        services.AddScoped<IChainRunRepository, EfChainRunRepository>();
        services.AddScoped<IEventRepository, EfEventRepository>();

        // Embeddings require an explicitly configured provider.
        void ConfigureEmbeddings()
        {
            if (!string.IsNullOrWhiteSpace(configuration["PlaceContext:Voyage:ApiKey"]))
            {
                services.AddSingleton<IEmbeddingGateway, Embeddings.RemoteEmbeddingGateway>();
                return;
            }
            services.AddSingleton<IEmbeddingGateway, Embeddings.NullEmbeddingGateway>();
        }
        ConfigureEmbeddings();
        // Vector stores: Qdrant when an endpoint is configured, otherwise pgvector (with lazy init).
        var qdrantUrl2 = configuration["PlaceContext:Qdrant:Endpoint"];
        void ConfigureVectorStorage()
        {
            if (!string.IsNullOrWhiteSpace(qdrantUrl2))
            {
                services.AddScoped<IRunEmbeddingRepository>(sp =>
                {
                    var embeddings = sp.GetRequiredService<IEmbeddingGateway>();
                    var tenant = sp.GetRequiredService<ICurrentTenant>();
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    return new VectorStore.VectorRunEmbeddingRepository(embeddings, tenant, httpFactory, qdrantUrl2, sp.GetRequiredService<IDataEncryptor>());
                });
                services.AddScoped<IContentIndexer>(sp =>
                {
                    var gateway = sp.GetRequiredService<IEmbeddingGateway>();
                    var tenant = sp.GetRequiredService<ICurrentTenant>();
                    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                    return new VectorStore.VectorContentIndexer(gateway, tenant, httpFactory, qdrantUrl2, sp.GetRequiredService<IDataEncryptor>());
                });
                // One-shot migration from pgvector tables on first startup.
                services.AddHostedService<VectorStore.VectorIndexMigrationHostedService>();
                return;
            }
            services.AddScoped<IRunEmbeddingRepository, Persistence.EfRunEmbeddingRepository>();
            services.AddScoped<IContentIndexer, Embeddings.ContentIndexer>();
        }
        ConfigureVectorStorage();
        services.AddScoped<Domain.Repositories.IMcpConnectionRepository, Persistence.EfMcpConnectionRepository>();

        // Dependency-graph assembly is expensive (full ledger + decisions + O(n²) embedding weave);
        // wrap the Application provider in a short-TTL cache so page opens and brain rollups don't
        // recompute it every time. Registered after AddApplication, so this mapping wins resolution.
        services.AddMemoryCache();
        services.AddScoped<Application.Features.DecisionTreeProvider>();
        services.AddScoped<Application.Features.IDecisionTreeProvider, Caching.CachedDecisionTreeProvider>();
        services.AddHostedService<Caching.DecisionTreeCacheBackgroundService>();

        // Trigger scheduling: cron evaluation, a durable DB-backed run queue, and the background firing
        // service (advisory-lock-elected schedule scan + atomic queue drain — correct across replicas).
        services.AddSingleton<ICronSchedule, Scheduling.CronosCronSchedule>();
        services.AddScoped<IJobRunQueue, Scheduling.DbJobRunQueue>();
        services.AddScoped<IJobRunSubmissionQueue, Scheduling.DbJobRunSubmissionQueue>();
        services.AddScoped<IJobChainSubmissionQueue, Scheduling.DbJobChainSubmissionQueue>();
        services.AddScoped<Scheduling.DbChainQueueService>();
        services.AddScoped<IChainQueueService>(sp => sp.GetRequiredService<Scheduling.DbChainQueueService>());
        services.AddScoped<IAppQueueGateway, ClusterAppQueueGateway>();
        services.AddHostedService<Scheduling.TriggerSchedulerBackgroundService>();
        services.AddHostedService<Scheduling.ChainContinuationBackgroundService>();
        services.AddHostedService<Scheduling.RpcChainSubmissionBackgroundService>();
        services.AddHostedService<Scheduling.RpcJobSubmissionBackgroundService>();

        // Background portal operations (the notifications-pane ledger) + the analytics chart sweep
        // worker (generation can be slow; the portal only enqueues and reads stored charts).
        services.AddSingleton(sp => new PlaceContext.Application.Operations.OperationCenter(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PlaceContext.Application.Operations.OperationCenter>>()));
        services.AddSingleton<Scheduling.AnalyticsRefreshQueue>();
        services.AddSingleton<IAnalyticsRefreshQueue>(sp => sp.GetRequiredService<Scheduling.AnalyticsRefreshQueue>());
        services.AddHostedService<Scheduling.AnalyticsBackgroundService>();

        // Run-status watcher: syncs persisted job/chain run statuses into the notifications pane on
        // a short tick, so the bell reflects finish/fail the moment the row commits — independent of
        // the (slow, best-effort) in-process enrichment and of which replica executed the run.
        services.AddScoped<IRunStatusReader, DbRunStatusReader>();
        services.AddScoped<Scheduling.DbRunStatusReconciler>();
        services.AddHostedService<Scheduling.FailedRunReconciliationBackgroundService>();
        services.AddSingleton<IRunStatusNotifier, Operations.OperationCenterRunStatusNotifier>();
        services.AddHostedService<Scheduling.RunStatusWatcherBackgroundService>();

        // Each project's own database (Postgres schema + role isolation; Monaco SQL in the portal).
        // A project's external database override resolves through the Vault (cluster DB is the default).
        services.AddScoped<IProjectDatabaseConnectionResolver, ProjectData.ProjectDatabaseConnectionResolver>();
        services.AddScoped<ProjectData.SqlProjectDataStore>();
        services.AddScoped<IProjectDataStore>(sp => sp.GetRequiredService<ProjectData.SqlProjectDataStore>());
        services.AddScoped<IProjectDataReader>(sp => sp.GetRequiredService<ProjectData.SqlProjectDataStore>());

        // Cluster page: node inventory + fleet-master admin (promote / join codes over Tailscale).
        // Same in-cluster vs local detection as the workload runner above.
        void ConfigureServiceConsole()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            {
                services.AddSingleton<Cluster.ClusterInfoProvider>();
                services.AddSingleton<IClusterInfoProvider>(sp =>
                    sp.GetRequiredService<Cluster.ClusterInfoProvider>());
                services.AddSingleton<IClusterAdminPort>(sp =>
                    sp.GetRequiredService<Cluster.ClusterInfoProvider>());
                return;
            }
            services.AddSingleton<Cluster.LocalClusterInfoProvider>();
            services.AddSingleton<IClusterInfoProvider>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
            services.AddSingleton<IClusterAdminPort>(sp =>
                sp.GetRequiredService<Cluster.LocalClusterInfoProvider>());
        }
        ConfigureServiceConsole();
        // Mints fresh Tailscale auth keys from vaulted OAuth client credentials for agent join codes.
        services.AddSingleton<IPrivateNetworkKeyMinter, Cluster.PrivateNetworkKeyMinter>();

        // Agent join tokens — short-lived, one-time tokens exchanged for join codes from join.sh.
        services.AddSingleton<IAgentTokenManager, Cluster.InMemoryAgentTokenManager>();

        services.AddSingleton<Observability.JobTelemetryCollector>();
        services.AddSingleton<IJobTelemetryReader>(sp => sp.GetRequiredService<Observability.JobTelemetryCollector>());
        services.AddHostedService<Observability.JobTelemetryStartupHostedService>();

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
        EnsureRpcChainSubmissionSchema(db);
        EnsureRpcJobSubmissionSchema(db);
        EnsureWorkerNetworkingSchema(db);
        EnsureQemuReplaySchema(db);
        db.Database.ExecuteSqlRaw(DbVirtualMachineLoginStore.Schema);
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

    internal static void EnsureQemuReplaySchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS qemu_replay_frames (
                "TenantId" uuid NOT NULL,
                "RunId" uuid NOT NULL REFERENCES chain_runs("Id") ON DELETE CASCADE,
                "Step" integer NOT NULL CHECK ("Step" >= 0),
                "Sequence" integer NOT NULL CHECK ("Sequence" >= 0 AND "Sequence" <= 100),
                "Metadata" text NOT NULL,
                "Image" bytea,
                "ImageBytes" integer NOT NULL CHECK ("ImageBytes" >= 0 AND "ImageBytes" <= 3000000),
                PRIMARY KEY ("TenantId", "RunId", "Step", "Sequence")
            );
            CREATE INDEX IF NOT EXISTS ix_qemu_replay_run ON qemu_replay_frames ("RunId");
            """);
    }

    internal static void EnsureWorkerNetworkingSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS worker_networks (
                "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
                "Name" text NOT NULL, "Cidr" text NOT NULL, "Groups" jsonb NOT NULL, "Version" bigint NOT NULL,
                UNIQUE ("TenantId", "ProjectId", "Id"),
                FOREIGN KEY ("ProjectId") REFERENCES projects ("Id") ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS job_network_assignments (
                "TenantId" uuid NOT NULL, "ProjectId" uuid NOT NULL, "JobId" uuid NOT NULL,
                "NetworkId" uuid NOT NULL, "GroupId" uuid NOT NULL,
                PRIMARY KEY ("TenantId", "JobId"),
                FOREIGN KEY ("TenantId", "ProjectId", "NetworkId") REFERENCES worker_networks ("TenantId", "ProjectId", "Id") ON DELETE CASCADE,
                FOREIGN KEY ("JobId") REFERENCES jobs ("Id") ON DELETE CASCADE
            );
            """);
    }

    private static void EnsureRpcJobSubmissionSchema(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS rpc_job_submissions (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "JobId" uuid NOT NULL,
                "RunId" uuid NOT NULL,
                "InputPayload" text NULL,
                "Status" text NOT NULL,
                "Attempts" integer NOT NULL DEFAULT 0,
                "LastError" text NULL,
                "SubmittedAt" timestamptz NOT NULL,
                "NextAttemptAt" timestamptz NOT NULL,
                "StartedAt" timestamptz NULL,
                "FinishedAt" timestamptz NULL,
                "ClaimedBy" text NULL,
                "ClaimedAt" timestamptz NULL,
                "HeartbeatAt" timestamptz NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_rpc_job_submissions_tenant_run
                ON rpc_job_submissions ("TenantId", "RunId");
            CREATE INDEX IF NOT EXISTS ix_rpc_job_submissions_ready
                ON rpc_job_submissions ("Status", "NextAttemptAt", "SubmittedAt");
            """);
    }

    private static void EnsureRpcChainSubmissionSchema(AppDbContext db)
    {
        // This operational queue deliberately lives outside the EF aggregate model: workers claim it
        // globally, then restore tenant/user ambient context before entering the application layer.
        // Additive DDL keeps upgrades safe for existing clusters and creates the queue on fresh ones.
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS rpc_chain_submissions (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "ChainId" uuid NOT NULL,
                "ChainRunId" uuid NOT NULL UNIQUE,
                "IdempotencyKey" text NULL,
                "InputPayload" text NULL,
                "StepPayloadOverrides" text NULL,
                "RequestDigest" text NULL,
                "ReplayOfRunId" uuid NULL,
                "ReplayFromStepIndex" integer NULL,
                "SubmitterUserId" uuid NOT NULL,
                "SubmitterRole" text NOT NULL,
                "Status" text NOT NULL,
                "Attempts" integer NOT NULL DEFAULT 0,
                "LastError" text NULL,
                "SubmittedAt" timestamptz NOT NULL,
                "NextAttemptAt" timestamptz NOT NULL,
                "StartedAt" timestamptz NULL,
                "FinishedAt" timestamptz NULL,
                "ClaimedBy" text NULL,
                "ClaimedAt" timestamptz NULL,
                "HeartbeatAt" timestamptz NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_rpc_chain_submissions_idempotency
                ON rpc_chain_submissions ("TenantId", "IdempotencyKey")
                WHERE "IdempotencyKey" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_rpc_chain_submissions_pending
                ON rpc_chain_submissions ("Status", "NextAttemptAt", "SubmittedAt");
            CREATE TABLE IF NOT EXISTS chain_queues (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "ProjectId" uuid NOT NULL,
                "ChainId" uuid NOT NULL,
                "Name" text NOT NULL,
                "Enabled" boolean NOT NULL,
                "CreatedAt" timestamptz NOT NULL,
                "UpdatedAt" timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chain_queues_project ON chain_queues ("TenantId", "ProjectId");
            ALTER TABLE chain_queues ADD COLUMN IF NOT EXISTS "DeliveryDelaySeconds" integer NOT NULL DEFAULT 0;
            ALTER TABLE chain_queues ADD COLUMN IF NOT EXISTS "MaxAttempts" integer NOT NULL DEFAULT 3;
            ALTER TABLE chain_queues ADD COLUMN IF NOT EXISTS "RetryDelaySeconds" integer NOT NULL DEFAULT 3;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "StepPayloadOverrides" text NULL;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "RequestDigest" text NULL;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "ReplayOfRunId" uuid NULL;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "ReplayFromStepIndex" integer NULL;
             ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "MaxAttempts" integer NOT NULL DEFAULT 3;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "RetryDelaySeconds" integer NOT NULL DEFAULT 3;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "MessageTtlSeconds" integer NULL;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "PreviousRunIds" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[];
            ALTER TABLE chain_queues ADD COLUMN IF NOT EXISTS "MessageTtlSeconds" integer NOT NULL DEFAULT 86400;
            ALTER TABLE chain_queues ADD COLUMN IF NOT EXISTS "ReceiptRetentionDays" integer NOT NULL DEFAULT 30;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamptz NULL;
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "ReceiptRetentionDays" integer NOT NULL DEFAULT 30;
            CREATE INDEX IF NOT EXISTS ix_rpc_chain_submissions_expiry
                ON rpc_chain_submissions ("ExpiresAt") WHERE "Status" IN ('Queued', 'Running');
            ALTER TABLE rpc_chain_submissions ADD COLUMN IF NOT EXISTS "QueueId" uuid NULL;
            CREATE INDEX IF NOT EXISTS ix_rpc_chain_submissions_queue
                ON rpc_chain_submissions ("TenantId", "QueueId", "SubmittedAt" DESC);
            """);
    }

    private static void BackfillShardCounts(AppDbContext db, IServiceProvider sp)
    {
        try
        {
            var enc = sp.GetService<IDataEncryptor>();
            if (enc is null)
            {
                return;
            }
            var purpose = IDataEncryptor.Purpose.JobRun;
            var rows = db.JobRuns
                .FromSqlRaw("""SELECT * FROM job_runs WHERE "ShardCount" = 0 AND "ShardResultsJson" IS NOT NULL""")
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }
            foreach (var row in rows)
            {
                try
                {
                    var json = enc.Unprotect(row.ShardResultsJson, purpose);
                    var shards = System.Text.Json.JsonSerializer.Deserialize<List<ShardResultDto>>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (shards is null)
                    {
                        continue;
                    }
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

}
