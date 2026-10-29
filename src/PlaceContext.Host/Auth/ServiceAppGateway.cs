using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Tenancy;
using PlaceContext.Infrastructure.Tenancy;
using Yarp.ReverseProxy.Forwarder;

namespace PlaceContext.Host.Auth;

public sealed class ServiceAppGateway(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ServiceAppSso sso, ITenantStore tenants, IServiceConsole services,
        IHttpForwarder forwarder, ServiceAppForwarder transport)
    {
        if (!context.Request.Host.Host.Contains(".apps.", StringComparison.OrdinalIgnoreCase)) { await next(context); return; }
        if (!sso.TryApp(context.Request.Host.Host, out var project, out var service)) { context.Response.StatusCode = 404; return; }
        if (sso.PortalOrigin.Scheme == "https" && !context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.Path == "/__pc/sso")
        {
            var grant = sso.Consume(context.Request.Query["code"].ToString(), context.Request.Cookies[ServiceAppSso.StateCookie], project, service);
            if (grant is null) { context.Response.StatusCode = 401; return; }
            context.Response.Cookies.Delete(ServiceAppSso.StateCookie, ServiceAppSso.Cookie(context, TimeSpan.Zero));
            context.Response.Cookies.Append(ServiceAppSso.SessionCookie, sso.Protect(grant.Session), ServiceAppSso.Cookie(context, TimeSpan.FromMinutes(15)));
            context.Response.Redirect(grant.ReturnPath);
            return;
        }
        var session = sso.Read(context.Request.Cookies[ServiceAppSso.SessionCookie], project, service);
        if (session is null)
        {
            if (!HttpMethods.IsGet(context.Request.Method) || !context.Request.Headers.Accept.ToString().Contains("text/html"))
            { context.Response.StatusCode = 401; return; }
            var state = ServiceAppSso.RandomToken();
            context.Response.Cookies.Append(ServiceAppSso.StateCookie, state, ServiceAppSso.Cookie(context, TimeSpan.FromMinutes(5)));
            var path = $"/services/sso/{project:D}/{service:D}";
            context.Response.Redirect(QueryHelpers.AddQueryString(new Uri(sso.PortalOrigin, path).AbsoluteUri,
                new Dictionary<string, string?> { ["state"] = state, ["returnPath"] = context.Request.Path + context.Request.QueryString }));
            return;
        }
        // App origins never execute portal routes or receive the portal's login cookie.
        var tenant = await tenants.FindBySlugAsync(TenantResolutionMiddleware.ResolveSlug(sso.PortalOrigin.Host), context.RequestAborted);
        if (tenant?.Id != session.TenantId) { context.Response.StatusCode = 403; return; }
        CurrentTenant.Set(tenant);
        CurrentUser.Set(new(session.UserId, session.Role));
        try
        {
            var destination = await services.GetSsoEndpointAsync(project, service, context.RequestAborted);
            // Reject cross-origin writes and socket upgrades even when browsers regard sibling apps as same-site.
            var origin = context.Request.Headers.Origin.ToString();
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) || context.WebSockets.IsWebSocketRequest)
                && !string.Equals(origin, sso.AppOrigin(project, service).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = 403; return; }
            context.Items["ServiceAppUser"] = session.UserId.ToString("D");
            await forwarder.SendAsync(context, destination.AbsoluteUri, transport.Client,
                new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(100) }, transport);
        }
        catch (UnauthorizedAccessException) { context.Response.StatusCode = 403; }
        catch (KeyNotFoundException) { context.Response.StatusCode = 404; }
        catch (InvalidOperationException) { context.Response.StatusCode = 503; }
        finally { CurrentUser.Clear(); CurrentTenant.Clear(); }
    }
}
