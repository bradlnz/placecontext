using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Infrastructure.Communications;
using PlaceContext.Identity.Infrastructure.OAuth;
using PlaceContext.Identity.OAuth;
using PlaceContext.Infrastructure;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Identity;

public static class IdentityInfrastructureDependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);
        services.RemoveAll<CommunicationProviderService>();
        services.RemoveAll<DatabaseCommunicationSender>();
        services.RemoveAll<IClientCommunicationSender>();
        services.AddScoped<IClientCommunicationSender, HttpIdentityCommunicationSender>();

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
