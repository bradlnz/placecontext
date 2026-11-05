using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;
public sealed record RepeatFlowChainActionView(string UntilCondition, int MaxAttempts = 3)
    : ChainActionView(RepeatFlowChainAction.ActionType, "Repeat flow");
