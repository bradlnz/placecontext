using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class DecisionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Question { get; set; } = "";
    public string Choice { get; set; } = "";
    public string Rationale { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; }
}
