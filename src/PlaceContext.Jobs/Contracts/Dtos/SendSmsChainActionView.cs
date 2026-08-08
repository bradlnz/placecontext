using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record SendSmsChainActionView(string Recipient, string Body)
    : ChainActionView(SendSmsChainAction.ActionType, "Send SMS");
