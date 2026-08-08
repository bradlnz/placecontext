using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;
using PlaceContext.Vault.Infrastructure.Persistence;
using PlaceContext.Vault.Infrastructure.Security;

namespace PlaceContext.Vault;

public static class VaultInfrastructureDependencyInjection
{
    public static IServiceCollection AddVaultInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Vault")
            ?? configuration[$"{VaultPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? VaultPersistenceOptions.DefaultConnectionString;

        services.Configure<VaultPersistenceOptions>(
            configuration.GetSection(VaultPersistenceOptions.SectionName));
        services.Configure<VaultDataProtectionOptions>(
            configuration.GetSection(VaultDataProtectionOptions.SectionName));

        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration[$"{VaultDataProtectionOptions.SectionName}:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

        services.AddDbContext<VaultDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Vault"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<VaultDbContext>("vault-database");
        services.AddScoped<IVaultUnitOfWork>(provider =>
            provider.GetRequiredService<VaultDbContext>());
        services.AddScoped<IProjectSecretRepository, EfProjectSecretRepository>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        return services;
    }
}
