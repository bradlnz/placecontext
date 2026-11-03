using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace PlaceContext.Host.Auth;

// shortcut: handoffs are local to the Docker host; use a shared single-use store before multi-host ingress.
public sealed class ServiceAppSso(IConfiguration config, IWebHostEnvironment environment, IDataProtectionProvider protection, IMemoryCache cache)
{
    public const string SessionCookie = "pc_app_session";
    public const string StateCookie = "pc_app_state";
    private readonly object _gate = new();
    private readonly IDataProtector _protector = protection.CreateProtector("PlaceContext.ServiceApp.Session.v1");
    public Uri PortalOrigin
    {
        get
        {
            var value = config["PlaceContext:Services:PortalOrigin"] ?? (environment.IsDevelopment() ? "http://localhost:7700" : null);
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0
                || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.HostNameType != UriHostNameType.Dns
                || (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http" && uri.Host == "localhost")))
            {
                throw new InvalidOperationException("Configure PlaceContext:Services:PortalOrigin as the HTTPS portal origin before using app SSO.");
            }
            return uri;
        }
    }
    public Uri AppOrigin(Guid project, Guid service) => new UriBuilder(PortalOrigin) { Host = $"{service:N}.{project:N}.apps.{PortalOrigin.Host}" }.Uri;
    public bool TryApp(string host, out Guid project, out Guid service)
    {
        project = service = Guid.Empty;
        var suffix = ".apps." + PortalOrigin.Host;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var ids = host[..^suffix.Length].Split('.');
        return ids.Length == 2 && Guid.TryParseExact(ids[0], "N", out service) && Guid.TryParseExact(ids[1], "N", out project);
    }
    public static string LocalPath(string? path) => path is { Length: > 0 and <= 4096 } && path[0] == '/'
        && !path.StartsWith("//") && !path.Contains('\\') && !path.Any(char.IsControl) ? path : "/";
    public static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string Issue(ServiceAppSession session, string state, string path)
    {
        var code = RandomToken();
        cache.Set("app-sso:" + code, new ServiceAppGrant(session, state, LocalPath(path)), TimeSpan.FromMinutes(1));
        return code;
    }
    internal ServiceAppGrant? Consume(string code, string? state, Guid project, Guid service)
    {
        if (code.Length != 64 || state?.Length != 64)
        {
            return null;
        }
        lock (_gate)
        {
            if (!cache.TryGetValue<ServiceAppGrant>("app-sso:" + code, out var grant) || grant is null
                || grant.Session.ProjectId != project || grant.Session.ServiceId != service || grant.State != state
                || grant.Session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return null;
            }
            cache.Remove("app-sso:" + code);
            return grant;
        }
    }
    internal string Protect(ServiceAppSession session) => _protector.Protect(JsonSerializer.Serialize(session));
    internal ServiceAppSession? Read(string? value, Guid project, Guid service)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        try
        {
            var session = JsonSerializer.Deserialize<ServiceAppSession>(_protector.Unprotect(value));
            return session?.ProjectId == project && session.ServiceId == service && session.ExpiresAt > DateTimeOffset.UtcNow ? session : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }
    internal static CookieOptions Cookie(HttpContext context, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = lifetime
    };
}
