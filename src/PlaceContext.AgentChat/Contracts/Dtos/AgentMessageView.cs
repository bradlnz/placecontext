namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a single chat message.</summary>
public sealed record AgentMessageView(string Role, string Content, DateTimeOffset Timestamp);
