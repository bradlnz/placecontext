using Microsoft.AspNetCore.Components.Server.Circuits;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Re-applies the circuit's tenant to the ambient <see cref="CurrentTenant"/> before each inbound
/// Blazor activity (render/event). Without this, AsyncLocal set during the initial HTTP request would
/// not flow into later circuit callbacks, and interactive queries would lose their tenant scope.
/// </summary>
public sealed class TenantCircuitHandler : CircuitHandler
{
    private readonly TenantHolder _holder;
    public TenantCircuitHandler(TenantHolder holder) => _holder = holder;

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            if (_holder.Tenant is { } tenant)
                CurrentTenant.Set(tenant);
            await next(context);
        };
}
