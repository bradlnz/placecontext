namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentSubscription(string AgentId, bool Active, DateTimeOffset UpdatedAt);
