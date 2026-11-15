using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record LoopChainActionView(string Kind, string Instructions, string ResponseField, string ResponseType = LoopChainAction.TextResponse,
    string AgentProvider = LoopChainAction.ExternalAgent, Guid? ConnectionId = null, string? Model = null, Guid? AgentId = null)
    : ChainActionView(Kind, Kind == LoopChainAction.TaskType ? "Agent task" : Kind == LoopChainAction.HumanType ? "Human in the loop" : "Agent review");
