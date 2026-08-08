using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Vault;

public static class DependencyInjection
{
    public static IServiceCollection AddVaultApi(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<AddProjectSecretCommand, ProjectSecretView>, AddProjectSecretHandler>();
        services.AddScoped<ICommandHandler<DeleteProjectSecretCommand, bool>, DeleteProjectSecretHandler>();
        services.AddScoped<IQueryHandler<ListProjectSecretsQuery, IReadOnlyList<ProjectSecretView>>, ListProjectSecretsHandler>();
        return services;
    }

    public static IServiceCollection AddVaultModule(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<AddProjectSecretCommand, ProjectSecretView>, AddProjectSecretHandler>();
        services.AddScoped<ICommandHandler<DeleteProjectSecretCommand, bool>, DeleteProjectSecretHandler>();
        services.AddScoped<IQueryHandler<ListProjectSecretsQuery, IReadOnlyList<ProjectSecretView>>, ListProjectSecretsHandler>();
        return services;
    }
}
