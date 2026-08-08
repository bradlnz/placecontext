using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;

namespace PlaceContext.App.Proxy;

public sealed class MicroserviceProxyRoute
{
    private readonly PathString? _pathPrefix;
    private readonly Regex? _pathPattern;

    private MicroserviceProxyRoute(
        string serviceName,
        PathString? pathPrefix,
        Regex? pathPattern)
    {
        ServiceName = serviceName;
        _pathPrefix = pathPrefix;
        _pathPattern = pathPattern;
    }

    public string ServiceName { get; }

    public static MicroserviceProxyRoute ForPrefix(string serviceName, string pathPrefix)
        => new(serviceName, new PathString(pathPrefix), null);

    public static MicroserviceProxyRoute ForPattern(string serviceName, string pathPattern)
        => new(serviceName, null, new Regex(
            pathPattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));

    public bool Matches(PathString path)
        => _pathPrefix is { } prefix
            ? path.StartsWithSegments(prefix)
            : _pathPattern!.IsMatch(path.Value ?? string.Empty);
}
