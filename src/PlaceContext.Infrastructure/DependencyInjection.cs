using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Risk;
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
        services.AddScoped<IWorkItemRepository, EfWorkItemRepository>();
        services.AddScoped<IReportTemplateRepository, EfReportTemplateRepository>();

        // Git, metrics, skill scaffolding.
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<ICodeMetricsProbe, FileScanCodeMetricsProbe>();
        services.AddSingleton<ISkillScaffolder, FileSkillScaffolder>();
        services.AddSingleton<IRepoFiles, Files.FileRepoFiles>();

        // GitHub OAuth + repo import.
        services.AddHttpClient();
        services.AddSingleton<IGitHubGateway, GitHub.GitHubGateway>();
        services.AddSingleton<ICodeWorkspace, Git.CodeWorkspace>();

        // LLM gateway (report polish + job-output organization). Provider-configurable:
        //   PlaceContext:Llm:Provider = "anthropic" | "ollama" | "none".
        // When unset, default to anthropic if an API key is present, else none (back-compat).
        var hasLlmKey = !string.IsNullOrWhiteSpace(configuration["PlaceContext:Llm:ApiKey"]);
        var llmProvider = (configuration["PlaceContext:Llm:Provider"] ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(llmProvider))
            llmProvider = hasLlmKey ? "anthropic" : "none";

        switch (llmProvider)
        {
            case "anthropic":
                services.AddSingleton<ILlmGateway, Llm.AnthropicLlmGateway>();
                break;
            case "ollama":
                services.AddSingleton<ILlmGateway, Llm.OllamaLlmGateway>();
                break;
            default:
                services.AddSingleton<ILlmGateway, Llm.NullLlmGateway>();
                break;
        }

        // Risk strategies behind a factory (domain scorers come from AddApplication()).
        services.AddScoped<IRiskCalculator, TechnicalRiskCalculator>();
        services.AddScoped<IRiskCalculator, ProcessRiskCalculator>();
        services.AddScoped<IRiskCalculatorFactory, RiskCalculatorFactory>();

        // Generic workload runner. In-cluster (the Host pod has KUBERNETES_SERVICE_HOST) we run jobs as
        // Kubernetes Jobs via the API + the Host's ServiceAccount/RBAC; otherwise (local dev) Docker.
        services.Configure<WorkloadRunnerOptions>(
            configuration.GetSection("PlaceContext:WorkloadRunner"));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            services.AddSingleton<IWorkloadRunner, Workload.KubernetesWorkloadRunner>();
        else
            services.AddSingleton<IWorkloadRunner, DockerWorkloadRunner>();

        // Job / JobRun repositories.
        services.AddScoped<IJobRepository, EfJobRepository>();
        services.AddScoped<IJobRunRepository, EfJobRunRepository>();

        // Trigger + event repositories.
        services.AddScoped<IJobTriggerRepository, EfJobTriggerRepository>();
        services.AddScoped<IEventRepository, EfEventRepository>();

        // Embeddings: Voyage AI when a key is configured, else a graceful no-op. The pgvector-backed
        // run-embedding store self-initializes lazily and degrades if the extension is unavailable.
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
