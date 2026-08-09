using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Identity.Infrastructure.Persistence;

/// <summary>Marker for a row owned by a tenant — gets a <c>TenantId</c> column, a global query filter,
/// and automatic stamping on insert.</summary>
public interface IIdentityTenantOwned
{
    Guid TenantId { get; set; }
}
