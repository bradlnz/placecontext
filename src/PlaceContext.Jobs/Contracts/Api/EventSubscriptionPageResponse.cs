namespace PlaceContext.Jobs.Contracts.Api;

public sealed record EventSubscriptionPageResponse(Guid Id, string? EventName, bool Enabled);
