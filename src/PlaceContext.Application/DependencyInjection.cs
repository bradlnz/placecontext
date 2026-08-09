using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.BuildingBlocks;

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
        return services.AddPlaceContextCqrs();
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddApplicationCore();
        services.AddScoped<IPlaceContextService, PlaceContextService>();

        // Commands.
        services.AddScoped<ICommandHandler<ScaffoldSkillCommand, SkillScaffoldView>, ScaffoldSkillHandler>();
        services.AddScoped<ICommandHandler<SetupHermesCommand, SkillScaffoldView>, SetupHermesHandler>();



        return services;
    }
}
