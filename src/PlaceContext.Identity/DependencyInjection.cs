using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.BuildingBlocks;
using PlaceContext.Identity.Access;
using PlaceContext.Identity.Auth;

namespace PlaceContext.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddPlaceContextCqrs();
        services.AddScoped<IIdentityAccessService, IdentityAccessService>();
        services.AddScoped<IQueryHandler<GetUserPermissionsQuery, UserPermissionsView>, GetUserPermissionsHandler>();
        services.AddScoped<ICommandHandler<SetUserPermissionOverrideCommand, UserPermissionsView>, SetUserPermissionOverrideHandler>();
        services.AddScoped<IQueryHandler<ListRolesQuery, IReadOnlyList<RoleView>>, ListRolesHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, RoleView>, CreateRoleHandler>();
        services.AddScoped<ICommandHandler<UpdateRolePermissionsCommand, RoleView>, UpdateRolePermissionsHandler>();
        services.AddScoped<ICommandHandler<DeleteRoleCommand, bool>, DeleteRoleHandler>();
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
