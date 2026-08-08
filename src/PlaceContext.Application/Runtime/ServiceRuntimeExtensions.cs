using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

public static class ServiceRuntimeExtensions
{
    public static IServiceCollection AddPlaceContextServiceRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly controllerAssembly)
    {
        services.TryAddSingleton<ServiceCurrentTenant>();
        services.TryAddScoped<IRequestTenantResolver, NullRequestTenantResolver>();
        services.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(provider =>
            provider.GetRequiredService<ServiceCurrentTenant>()));
        services.Replace(ServiceDescriptor.Singleton<ICurrentTenantAccessor>(provider =>
            provider.GetRequiredService<ServiceCurrentTenant>()));
        services.TryAddSingleton<ServiceCurrentUser>();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUser>(provider =>
            provider.GetRequiredService<ServiceCurrentUser>()));
        services.Replace(ServiceDescriptor.Singleton<ICurrentUserAccessor>(provider =>
            provider.GetRequiredService<ServiceCurrentUser>()));
        services.Replace(ServiceDescriptor.Singleton<IClock, ServiceSystemClock>());

        var auth = configuration.GetSection(ServiceAuthenticationDefaults.SectionName);
        var authority = auth["Authority"];
        var audience = auth["Audience"] ?? "placecontext-services";
        var signingKey = auth["SigningKey"];

        if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"Configure {ServiceAuthenticationDefaults.SectionName}:Authority or provide SigningKey through a secret source.");
        }

        services
            .AddAuthentication(ServiceAuthenticationDefaults.Scheme)
            .AddJwtBearer(ServiceAuthenticationDefaults.Scheme, options =>
            {
                options.MapInboundClaims = false;
                options.Audience = audience;

                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                    options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);
                    return;
                }

                if (Encoding.UTF8.GetByteCount(signingKey!) < 32)
                    throw new InvalidOperationException("ServiceAuth:SigningKey must contain at least 32 UTF-8 bytes.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey!)),
                    ValidateIssuer = true,
                    ValidIssuer = auth["Issuer"] ?? "placecontext",
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ServiceApiKeyAuthenticationHandler>(
                ServiceApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(ServiceAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .Build();

            foreach (var permission in Permission.All)
            {
                options.AddPolicy(permission, policy => policy
                    .AddAuthenticationSchemes(ServiceAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(ServiceAuthenticationDefaults.PermissionClaim, permission));
            }
        });

        services.AddControllers().AddApplicationPart(controllerAssembly);
        services.AddHealthChecks();
        return services;
    }

    public static WebApplication UsePlaceContextServiceRuntime(
        this WebApplication app,
        string serviceName)
    {
        app.UseAuthentication();
        app.UseMiddleware<ServiceRequestContextMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapGet("/", () => Results.Ok(new { service = serviceName, status = "ready" }))
            .AllowAnonymous();
        return app;
    }
}
