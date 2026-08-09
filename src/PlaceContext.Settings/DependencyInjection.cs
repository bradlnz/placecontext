using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Settings;

public static class DependencyInjection
{
    public static IServiceCollection AddSettingsModule(this IServiceCollection services) => services;
}
