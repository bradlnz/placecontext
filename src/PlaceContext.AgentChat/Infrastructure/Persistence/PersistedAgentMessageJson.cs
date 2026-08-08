namespace PlaceContext.AgentChat.Infrastructure.Persistence;

internal sealed record PersistedAgentMessageJson(string Role, string Content, long Timestamp);
