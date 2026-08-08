namespace PlaceContext.Crm.Automation;

public sealed record CrmAutomationReceipt(
    Guid TrackingId,
    Guid RuleId,
    Guid ChainId,
    string RuleName);
