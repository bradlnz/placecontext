using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class DebtAssessmentRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public double Technical { get; set; }
    public double Agentic { get; set; }
    public string Signals { get; set; } = "[]";
    public DateTimeOffset ComputedAt { get; set; }
}
