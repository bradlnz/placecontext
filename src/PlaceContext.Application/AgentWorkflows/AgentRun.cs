namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentRun(Guid Id, string AgentId, AgentRunStatus Status, string? Output,
    string? Notice, long? InputTokens, long? OutputTokens, decimal? DemoCredits,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);
