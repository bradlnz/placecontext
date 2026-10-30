using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class AgentRunRow : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string AgentId { get; set; } = "";
    public string InputHash { get; set; } = "";
    public AgentRunStatus Status { get; set; }
    public string? Output { get; set; }
    public string? Notice { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? DemoCredits { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
