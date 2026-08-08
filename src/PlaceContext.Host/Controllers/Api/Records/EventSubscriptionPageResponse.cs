namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EventSubscriptionPageResponse(Guid Id, string? EventName, bool Enabled);
