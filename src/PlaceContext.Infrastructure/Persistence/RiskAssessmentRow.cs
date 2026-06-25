using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class RiskAssessmentRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public double Technical { get; set; }
    public double Process { get; set; }
    public string Signals { get; set; } = "[]";
    public DateTimeOffset ComputedAt { get; set; }
}
