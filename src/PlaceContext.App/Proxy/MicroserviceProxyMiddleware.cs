using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PlaceContext.App.Proxy;

public sealed class MicroserviceProxyMiddleware
{
    public const string HttpClientName = "PlaceContextMicroserviceProxy";

    private static readonly HashSet<string> ExcludedRequestHeaders = new(
        StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.Connection,
        HeaderNames.Cookie,
        HeaderNames.Host,
        HeaderNames.KeepAlive,
        HeaderNames.ProxyAuthenticate,
        HeaderNames.ProxyAuthorization,
        HeaderNames.TE,
        HeaderNames.Trailer,
        HeaderNames.TransferEncoding,
        HeaderNames.Upgrade,
        "Proxy-Connection",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
    };

    private static readonly HashSet<string> ExcludedResponseHeaders = new(
        StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.Connection,
        HeaderNames.KeepAlive,
        HeaderNames.ProxyAuthenticate,
        HeaderNames.ProxyAuthorization,
        HeaderNames.TE,
        HeaderNames.Trailer,
        HeaderNames.TransferEncoding,
        HeaderNames.Upgrade,
        HeaderNames.SetCookie,
        HeaderNames.Server,
        "Proxy-Connection",
    };

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MicroserviceProxyOptions _options;
    private readonly ILogger<MicroserviceProxyMiddleware> _logger;

    public MicroserviceProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IOptions<MicroserviceProxyOptions> options,
        ILogger<MicroserviceProxyMiddleware> logger)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var route = MicroserviceProxyRouteCatalog.All.FirstOrDefault(candidate =>
            candidate.Matches(context.Request.Path));

        if (route is null)
        {
            await _next(context);
            return;
        }

        if (!TryResolveDestination(route, out var destination))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { error = $"The {route.ServiceName} microservice is not configured." },
                context.RequestAborted);
            return;
        }

        await ForwardAsync(context, route, destination);
    }

    private bool TryResolveDestination(
        MicroserviceProxyRoute route,
        out Uri destination)
    {
        destination = null!;
        if (!_options.Destinations.TryGetValue(route.ServiceName, out var configured)
            || !Uri.TryCreate(configured, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        destination = parsed;
        return true;
    }

    private async Task ForwardAsync(
        HttpContext context,
        MicroserviceProxyRoute route,
        Uri destination)
    {
        var target = BuildTargetUri(destination, context.Request);
        using var proxyRequest = CreateProxyRequest(context, route, target);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var proxyResponse = await client.SendAsync(
                proxyRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            await CopyResponseAsync(context, route, proxyResponse, destination);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to proxy {Method} {Path} to {ServiceName} at {Destination}",
                context.Request.Method,
                context.Request.Path,
                route.ServiceName,
                destination);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(
                    new { error = $"The {route.ServiceName} microservice is unavailable." },
                    context.RequestAborted);
            }
        }
    }

    private static Uri BuildTargetUri(Uri destination, HttpRequest request)
    {
        var origin = destination.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? destination
            : new Uri(destination.AbsoluteUri + '/', UriKind.Absolute);
        var relativeTarget = $"{request.Path.ToUriComponent()}{request.QueryString.ToUriComponent()}"
            .TrimStart('/');
        return new Uri(origin, relativeTarget);
    }

    private static HttpRequestMessage CreateProxyRequest(
        HttpContext context,
        MicroserviceProxyRoute route,
        Uri target)
    {
        var request = context.Request;
        var message = new HttpRequestMessage(new HttpMethod(request.Method), target);

        if (RequestCanHaveBody(request))
            message.Content = new StreamContent(request.Body);

        foreach (var header in request.Headers)
        {
            if (ExcludedRequestHeaders.Contains(header.Key)
                && !(route.ServiceName == "Identity"
                    && header.Key.Equals(HeaderNames.Cookie, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        message.Headers.TryAddWithoutValidation(
            "X-Forwarded-Host",
            request.Host.Value);
        message.Headers.TryAddWithoutValidation(
            "X-Forwarded-Proto",
            request.Scheme);
        if (request.PathBase.HasValue)
        {
            message.Headers.TryAddWithoutValidation(
                "X-Forwarded-Prefix",
                request.PathBase.Value);
        }

        if (context.Connection.RemoteIpAddress is { } remoteAddress)
            message.Headers.TryAddWithoutValidation("X-Forwarded-For", remoteAddress.ToString());

        return message;
    }

    private static bool RequestCanHaveBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding);

    private static async Task CopyResponseAsync(
        HttpContext context,
        MicroserviceProxyRoute route,
        HttpResponseMessage proxyResponse,
        Uri destination)
    {
        context.Response.StatusCode = (int)proxyResponse.StatusCode;

        CopyHeaders(proxyResponse.Headers, context.Response.Headers);
        CopyHeaders(proxyResponse.Content.Headers, context.Response.Headers);
        if (route.ServiceName == "Identity"
            && proxyResponse.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies))
            context.Response.Headers[HeaderNames.SetCookie] = setCookies.ToArray();
        RewriteLocation(context, proxyResponse.Headers.Location, destination);

        foreach (var excluded in ExcludedResponseHeaders)
        {
            if (route.ServiceName != "Identity"
                || !excluded.Equals(HeaderNames.SetCookie, StringComparison.OrdinalIgnoreCase))
                context.Response.Headers.Remove(excluded);
        }

        await proxyResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static void RewriteLocation(
        HttpContext context,
        Uri? location,
        Uri destination)
    {
        if (location is null)
            return;

        if (!location.IsAbsoluteUri)
        {
            context.Response.Headers.Location = location.OriginalString;
            return;
        }

        if (!string.Equals(location.Scheme, destination.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(location.Host, destination.Host, StringComparison.OrdinalIgnoreCase)
            || location.Port != destination.Port)
        {
            return;
        }

        var publicOrigin = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
        context.Response.Headers.Location = $"{publicOrigin}{location.PathAndQuery}{location.Fragment}";
    }

    private static void CopyHeaders(
        System.Net.Http.Headers.HttpHeaders source,
        IHeaderDictionary destination)
    {
        foreach (var header in source)
        {
            if (!ExcludedResponseHeaders.Contains(header.Key))
                destination[header.Key] = header.Value.ToArray();
        }
    }
}
