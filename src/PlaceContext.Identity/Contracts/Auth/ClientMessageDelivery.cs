namespace PlaceContext.Application.Ports;

public sealed record ClientMessageDelivery(string Provider, string? ExternalId);
