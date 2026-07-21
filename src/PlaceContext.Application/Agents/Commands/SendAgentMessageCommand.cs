using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Sends a user message to the chat agent and returns the updated session with the assistant's reply.</summary>
public sealed record SendAgentMessageCommand(
    Guid ProjectId,
    Guid? SessionId,
    string Message) : ICommand<AgentChatSessionView>;
