using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.BuildingBlocks;

public static class DependencyInjection
{
    public static IServiceCollection AddPlaceContextCqrs(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
