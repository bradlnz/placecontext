using Microsoft.AspNetCore.Components.Server.Circuits;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Auth;

/// <summary>
/// Re-applies the circuit's resolved user to the ambient <see cref="CurrentUser"/> before each inbound
/// Blazor activity (render/event) — the AsyncLocal set by <see cref="UserResolutionMiddleware"/> during
/// the request that established the circuit does not flow into later circuit callbacks on its own. This
/// mirrors <c>TenantCircuitHandler</c>, which solves the identical problem for the ambient tenant.
/// </summary>
public sealed class UserCircuitHandler : CircuitHandler
{
    private readonly UserHolder _holder;
    public UserCircuitHandler(UserHolder holder) => _holder = holder;

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            if (_holder.User is { } user)
                CurrentUser.Set(user);
            await next(context);
        };
}
