using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PlaceContext.Host.Auth;

public sealed class LoginRateLimitPolicy : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext context)
        // shortcut: limits are per replica and peer IP; use a shared ingress limit before scaling replicas.
        => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
}
