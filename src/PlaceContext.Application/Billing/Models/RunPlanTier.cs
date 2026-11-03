namespace PlaceContext.Application.Billing;

public sealed record RunPlanTier(
    string Code, string Name, string PriceLabel,
    string Description, string Availability, string Support,
    bool SelfService, int? StartsPerMinute, IReadOnlyList<string> Features);
