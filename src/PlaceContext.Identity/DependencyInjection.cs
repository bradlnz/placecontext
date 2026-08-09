using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Identity.Auth;

namespace PlaceContext.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddSingleton<PortalToken>();
        services.AddScoped<ServiceTokenIssuer>();
        services.AddScoped<IAuthorizationHandler, DefaultAdminAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(
            IdentityPolicies.DefaultAdmin,
            policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new DefaultAdminRequirement())));
        services.AddAntiforgery();
        return services;
    }
}
