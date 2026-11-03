namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentWorkflowUsage(Guid ProjectId, string Model, long InputTokens, long OutputTokens, string Description, DateTimeOffset RecordedAt);
