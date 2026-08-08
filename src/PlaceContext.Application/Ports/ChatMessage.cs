namespace PlaceContext.Application.Ports;

/// <summary>A single message in a chat conversation.</summary>
public sealed record ChatMessage(string Role, string Content);
