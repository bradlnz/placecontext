using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Debt;
using PlaceContext.Infrastructure.Git;
using PlaceContext.Infrastructure.Metrics;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Skills;
using PlaceContext.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: binds options, opens the EF Core (PostgreSQL)
/// store, and wires every Application port to its adapter — the EF repositories, the git/metrics
/// adapters, the skill scaffolder, and the Strategy-behind-Factory debt calculators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaceContextOptions>(configuration.GetSection("PlaceContext"));

        services.AddSingleton<IClock, SystemClock>();

        // Multi-tenancy: ambient current-tenant (AsyncLocal singleton) + the tenant registry.
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantStore, EfTenantStore>();

        // Portal authentication (tenant-scoped users) + persisted OAuth clients.
        services.AddScoped<IAuthService, Auth.AuthService>();
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
        services.AddScoped<IChangeLedgerRepository, EfChangeLedgerRepository>();
        services.AddScoped<IDecisionRepository, EfDecisionRepository>();
        services.AddScoped<IDebtAssessmentRepository, EfDebtAssessmentRepository>();
        services.AddScoped<IProjectContextRepository, EfProjectContextRepository>();
        services.AddScoped<ICodeRequirementsRepository, EfCodeRequirementsRepository>();
        services.AddScoped<IUsageRepository, EfUsageRepository>();
        services.AddScoped<IWorkItemRepository, EfWorkItemRepository>();

        // Git, metrics, skill scaffolding.
        services.AddSingleton<IGitPort, CliGitAdapter>();
        services.AddSingleton<ICodeMetricsProbe, FileScanCodeMetricsProbe>();
        services.AddSingleton<ISkillScaffolder, FileSkillScaffolder>();
        services.AddSingleton<IRepoFiles, Files.FileRepoFiles>();

        // GitHub OAuth + repo import.
        services.AddHttpClient();
        services.AddSingleton<IGitHubGateway, GitHub.GitHubGateway>();
        services.AddSingleton<ICodeWorkspace, Git.CodeWorkspace>();

        // Debt strategies behind a factory (domain scorers come from AddApplication()).
        services.AddScoped<IDebtCalculator, TechnicalDebtCalculator>();
        services.AddScoped<IDebtCalculator, AgenticDebtCalculator>();
        services.AddScoped<IDebtCalculatorFactory, DebtCalculatorFactory>();

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
