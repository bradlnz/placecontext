using PlaceContext.Application.Dtos;

namespace PlaceContext.AgentChat.Contracts.Api;

public sealed record ChatPageResponse(
    AgentConfigView Config,
    IReadOnlyList<AgentChatSessionView> Sessions);
