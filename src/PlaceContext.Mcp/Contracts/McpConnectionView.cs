using System.Text.Json;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Dtos;

public sealed record McpConnectionView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Transport,
    string? EndpointUrl,
    string? Command,
    string? Args,
    string? AuthType,
    bool Enabled,
    string? LastStatus,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset CreatedAt,
    string? OAuthAccessToken = null,
    DateTimeOffset? OAuthTokenExpiresAt = null,
    string? OAuthClientId = null,
    string? OAuthScopes = null);
