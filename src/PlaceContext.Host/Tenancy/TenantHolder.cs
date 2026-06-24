using Microsoft.AspNetCore.Components.Server.Circuits;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>Holds the request/circuit's resolved tenant in scoped DI, so the Blazor circuit can re-apply it.</summary>
public sealed class TenantHolder
{
    public TenantInfo? Tenant { get; set; }
}
