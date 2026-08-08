namespace PlaceContext.Application.Ports;

/// <summary>Trusted caller identity propagated by an authenticated service request.</summary>
public sealed record UserContext(Guid Id, string Role);
