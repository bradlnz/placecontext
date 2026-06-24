using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class ProjectRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public string? GraphJson { get; set; }
    public double? TechnicalDebt { get; set; }
    public double? AgenticDebt { get; set; }
}
