using System.Net;
using PlaceContext.App.Authentication;

namespace PlaceContext.App.Proxy;

public static class MicroserviceProxyServiceCollectionExtensions
{
    public static IServiceCollection AddMicroserviceProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MicroserviceProxyOptions>(
            configuration.GetSection(MicroserviceProxyOptions.SectionName));
        services.AddScoped<EdgeServiceTokenClient>();
        services.AddScoped<EdgeCallerContext>();

        services
            .AddHttpClient(MicroserviceProxyMiddleware.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
            });

        return services;
    }
}
