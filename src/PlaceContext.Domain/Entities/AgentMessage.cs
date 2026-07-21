namespace PlaceContext.Domain.Entities;

/// <summary>
/// A single message within an <see cref="AgentChatSession"/>. Role is "user", "assistant", or "system".
/// </summary>
public sealed record AgentMessage(string Role, string Content, DateTimeOffset Timestamp);
