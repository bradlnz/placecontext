using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Application;

/// <summary>
/// Composition for the Application layer: the dispatcher, the pure domain services it needs, the
/// facade, and every command/query handler registered against its closed interface so the
/// reflection dispatcher can resolve it. Mirrors CodeRag's <c>AddApplication()</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddApplicationCore();
        services.AddScoped<IPlaceContextService, PlaceContextService>();

        // Commands.
        services.AddScoped<ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>, ScaffoldSkillHandler>();
        services.AddScoped<ICommandHandler<SetupHermesCommand, SkillScaffoldView>, SetupHermesHandler>();

        // Backup/restore (tenant settings + job definitions → a portable manifest).
        services.AddScoped<IQueryHandler<ExportManifestQuery, BackupManifest>, ExportManifestHandler>();
        services.AddScoped<ICommandHandler<ImportManifestCommand, ImportResultView>, ImportManifestHandler>();

        // Granular RBAC: a member's permission matrix (role defaults + tenant-scoped overrides).
        services.AddScoped<IQueryHandler<GetUserPermissionsQuery, UserPermissionsView>, GetUserPermissionsHandler>();
        services.AddScoped<ICommandHandler<SetUserPermissionOverrideCommand, UserPermissionsView>, SetUserPermissionOverrideHandler>();

        // Editable roles: list/create/update/delete role definitions (the Access "Roles & permissions" UI).
        services.AddScoped<IQueryHandler<ListRolesQuery, IReadOnlyList<RoleView>>, ListRolesHandler>();
        services.AddScoped<ICommandHandler<CreateRoleCommand, RoleView>, CreateRoleHandler>();
        services.AddScoped<ICommandHandler<UpdateRolePermissionsCommand, RoleView>, UpdateRolePermissionsHandler>();
        services.AddScoped<ICommandHandler<DeleteRoleCommand, bool>, DeleteRoleHandler>();


        return services;
    }
}
