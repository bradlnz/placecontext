using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Branding;
using PlaceContext.Settings.Configuration;

namespace PlaceContext.Settings;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<BrandingService>();
        services.AddScoped<MenuConfigService>();
        services.AddScoped<ArtifactViewConfigService>();
        services.AddScoped<IAuthorizationHandler, DefaultAdminAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(
            Policies.DefaultAdmin,
            policy => policy.RequireAuthenticatedUser().AddRequirements(new DefaultAdminRequirement())));
        return services;
    }
}
