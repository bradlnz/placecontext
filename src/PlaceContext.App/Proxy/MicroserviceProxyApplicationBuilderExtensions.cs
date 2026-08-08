namespace PlaceContext.App.Proxy;

public static class MicroserviceProxyApplicationBuilderExtensions
{
    public static IApplicationBuilder UseMicroserviceProxy(this IApplicationBuilder app) =>
        app.UseMiddleware<MicroserviceProxyMiddleware>();
}
