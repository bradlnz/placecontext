using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Mcp.Infrastructure.Persistence;
using PlaceContext.Mcp.Infrastructure.Security;

namespace PlaceContext.Mcp;

public static class McpInfrastructureDependencyInjection
{
    public static IServiceCollection AddMcpInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Mcp")
            ?? configuration[$"{McpPersistenceOptions.SectionName}:ConnectionString"]
            // Existing installs stored mcp_connections beside AgentChat. Keep that database as
            // the compatibility fallback until deployment explicitly supplies ConnectionStrings:Mcp.
            ?? configuration.GetConnectionString("AgentChat")
            ?? configuration["PlaceContext:ConnectionString"]
            ?? McpPersistenceOptions.DefaultConnectionString;

        services.Configure<McpPersistenceOptions>(
            configuration.GetSection(McpPersistenceOptions.SectionName));
        services.Configure<McpDataProtectionOptions>(
            configuration.GetSection(McpDataProtectionOptions.SectionName));

        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration[$"{McpDataProtectionOptions.SectionName}:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

        services.AddDbContext<McpDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Mcp"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks().AddDbContextCheck<McpDbContext>("mcp-database");
        services.AddScoped<IMcpUnitOfWork>(provider => provider.GetRequiredService<McpDbContext>());
        services.AddScoped<IMcpConnectionRepository, EfMcpConnectionRepository>();
        services.TryAddSingleton<IDataEncryptor, McpDataProtectionEncryptor>();
        services.AddHttpClient();
        return services;
    }
}
