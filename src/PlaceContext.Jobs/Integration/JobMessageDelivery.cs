namespace PlaceContext.Jobs.Integration;

public sealed record JobMessageDelivery(string Provider, string? ExternalId);
