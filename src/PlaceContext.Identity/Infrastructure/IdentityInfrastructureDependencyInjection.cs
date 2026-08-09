using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Identity.Domain.Persistence;
using PlaceContext.Identity.Domain.Tenants;
using PlaceContext.Identity.Infrastructure.Auth;
using PlaceContext.Identity.Infrastructure.Communications;
using PlaceContext.Identity.Infrastructure.OAuth;
using PlaceContext.Identity.Infrastructure.Persistence;
using PlaceContext.Identity.Infrastructure.Security;
using PlaceContext.Identity.Infrastructure.Tenancy;
using PlaceContext.Identity.OAuth;

namespace PlaceContext.Identity;

public static class IdentityInfrastructureDependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Identity")
            ?? configuration[$"{IdentityPersistenceOptions.SectionName}:ConnectionString"]
            ?? configuration["PlaceContext:ConnectionString"]
            ?? IdentityPersistenceOptions.DefaultConnectionString;

        services.Configure<IdentityPersistenceOptions>(options => options.ConnectionString = connectionString);
        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory_Identity"));
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("identity-database");
        services.AddScoped<IIdentityUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IIdentityTenantStore, EfIdentityTenantStore>();
        services.AddScoped<ITenantCatalog, EfTenantCatalog>();
        services.AddScoped<IRequestTenantResolver, IdentityRequestTenantResolver>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IUserApiTokenService, UserApiTokenService>();
        services.AddScoped<IOAuthClientStore, EfOAuthClientStore>();
        services.AddScoped<IOAuthRefreshTokenStore, EfOAuthRefreshTokenStore>();
        services.AddScoped<IOAuthAuthCodeStore, EfOAuthAuthCodeStore>();
        services.AddScoped<IUserPermissionGrantRepository, EfUserPermissionGrantRepository>();
        services.AddScoped<IRoleDefinitionRepository, EfRoleDefinitionRepository>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IClientCommunicationSender, HttpIdentityCommunicationSender>();

        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration["PlaceContext:Identity:DataProtection:KeyDirectory"];
        if (string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToDbContext<IdentityDbContext>();
        else
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        services.AddSingleton<IDataEncryptor, IdentityDataProtectionEncryptor>();

        services.AddAuthentication()
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "placecontext.identity";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/app/login";
                options.AccessDeniedPath = "/app/locked";
            });
        services.AddHttpClient();
        services.AddSingleton<IMcpOAuthConnectionClient, HttpMcpOAuthConnectionClient>();
        return services;
    }
}
