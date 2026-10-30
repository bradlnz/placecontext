namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentCompletion(string Text, string Model, long? InputTokens, long? OutputTokens);
