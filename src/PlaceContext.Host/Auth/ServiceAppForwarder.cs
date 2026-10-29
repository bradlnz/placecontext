using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Tenancy;
using PlaceContext.Infrastructure.Tenancy;
using Yarp.ReverseProxy.Forwarder;

namespace PlaceContext.Host.Auth;

public sealed class ServiceAppForwarder : HttpTransformer, IDisposable
{
    public HttpMessageInvoker Client { get; } = new(new SocketsHttpHandler
    { UseProxy = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10) });
    internal static bool ReservedCookie(string name) => name.StartsWith("pc_app_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("placecontext_", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".AspNetCore", StringComparison.OrdinalIgnoreCase);
    public override async ValueTask TransformRequestAsync(HttpContext context, HttpRequestMessage request, string prefix, CancellationToken ct)
    {
        await base.TransformRequestAsync(context, request, prefix, ct);
        request.Headers.Remove("Cookie");
        var cookies = context.Request.Headers.Cookie.ToString().Split(';').Where(part => !ReservedCookie(part.Trim().Split('=')[0]));
        var cookie = string.Join(';', cookies);
        if (cookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        foreach (var name in request.Headers.Select(header => header.Key).Where(name => name.StartsWith("X-PlaceContext-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)).ToArray()) request.Headers.Remove(name);
        request.Headers.Host = context.Request.Host.Value;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);
        request.Headers.TryAddWithoutValidation("X-PlaceContext-User-Id", context.Items["ServiceAppUser"]?.ToString());
    }
    public override async ValueTask<bool> TransformResponseAsync(HttpContext context, HttpResponseMessage? response, CancellationToken ct)
    {
        var result = await base.TransformResponseAsync(context, response, ct);
        // An app may set its own host cookies, never gateway cookies or cookies for sibling/portal hosts.
        if (SetCookieHeaderValue.TryParseList(context.Response.Headers.SetCookie.Select(value => value ?? "").ToArray(), out var cookies))
        {
            context.Response.Headers.Remove("Set-Cookie");
            foreach (var cookie in cookies.Where(cookie => !ReservedCookie(cookie.Name.ToString())))
            { cookie.Domain = default; context.Response.Headers.Append("Set-Cookie", cookie.ToString()); }
        }
        else context.Response.Headers.Remove("Set-Cookie");
        context.Response.Headers.CacheControl = "no-store";
        return result;
    }
    public void Dispose() => Client.Dispose();
}
