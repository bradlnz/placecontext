using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Marker for a row owned by a tenant — gets a <c>TenantId</c> column, a global query filter,
/// and automatic stamping on insert.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
