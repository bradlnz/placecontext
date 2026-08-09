using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Operations;

public static class DependencyInjection
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services) => services;
}
