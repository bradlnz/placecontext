namespace PlaceContext.Application.Connections;

public sealed record ManagedConnectionView(
    Guid ProjectId, string Provider, string Name, string Scopes, DateTimeOffset ExpiresAt);
