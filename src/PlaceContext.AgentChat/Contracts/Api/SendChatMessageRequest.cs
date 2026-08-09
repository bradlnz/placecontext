namespace PlaceContext.AgentChat.Contracts.Api;

public sealed record SendChatMessageRequest(Guid? SessionId, string? Message);
