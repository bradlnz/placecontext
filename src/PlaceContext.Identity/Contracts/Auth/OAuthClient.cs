using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>A dynamically-registered OAuth client (public, PKCE — no secret). Global, not tenant-scoped.</summary>
public sealed record OAuthClient(string ClientId, IReadOnlyList<string> RedirectUris, string Name);
