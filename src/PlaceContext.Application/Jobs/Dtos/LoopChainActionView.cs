using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record LoopChainActionView(string Kind, string Instructions, string ResponseField, string ResponseType = LoopChainAction.TextResponse,
    string AgentProvider = LoopChainAction.ExternalAgent)
    : ChainActionView(Kind, Kind == LoopChainAction.HumanType ? "Human in the loop" : "Agent in the loop");
