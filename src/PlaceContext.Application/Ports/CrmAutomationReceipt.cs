using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public sealed record CrmAutomationReceipt(
    Guid TrackingId,
    Guid RuleId,
    Guid ChainId,
    string RuleName);
