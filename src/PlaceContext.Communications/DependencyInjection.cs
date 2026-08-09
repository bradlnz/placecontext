using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Communications;

public static class DependencyInjection
{
    public static IServiceCollection AddCommunicationsModule(this IServiceCollection services) => services;
}
