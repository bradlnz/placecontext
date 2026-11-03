namespace PlaceContext.Application.Communications;

public sealed record ClientMessageDelivery(string Provider, string? ExternalId);
