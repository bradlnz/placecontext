namespace PlaceContext.Application.AgentWorkflows;

public sealed record AgentContextSource(string Name, string Kind, DateTimeOffset? UpdatedAt, string Summary);
