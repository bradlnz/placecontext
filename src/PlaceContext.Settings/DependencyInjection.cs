using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Data;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Branding;
using PlaceContext.Jobs;
using PlaceContext.Projects;
using PlaceContext.Vault;

namespace PlaceContext.Settings;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddApplication();
        services.AddProjectsModule();
        services.AddVaultModule();
        services.AddJobsModule();
        services.AddDataModule();
        services.AddScoped<BrandingService>();
        services.AddScoped<IAuthorizationHandler, DefaultAdminAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(
            Policies.DefaultAdmin,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new DefaultAdminRequirement())));
        return services;
    }
}
