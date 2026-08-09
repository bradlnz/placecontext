using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Identity.Infrastructure.OAuth;
using PlaceContext.Identity.OAuth;
using PlaceContext.Infrastructure;

namespace PlaceContext.Identity;

public static class IdentityInfrastructureDependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);

        var dataProtection = services.AddDataProtection().SetApplicationName("placecontext");
        var keyDirectory = configuration["PlaceContext:Identity:DataProtection:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

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
