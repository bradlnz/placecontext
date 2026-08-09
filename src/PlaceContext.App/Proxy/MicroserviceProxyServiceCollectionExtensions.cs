using System.Net;
using PlaceContext.App.Authentication;
using PlaceContext.App.Dashboard;
using PlaceContext.App.Workspace;

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
        services.AddScoped<EdgeHttpClient>();
        services.AddScoped<IWorkspaceHttpClient, WorkspaceHttpClient>();
        services.AddScoped<IDashboardHttpClient, DashboardHttpClient>();

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
