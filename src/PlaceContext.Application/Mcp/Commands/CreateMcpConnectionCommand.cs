using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record CreateMcpConnectionCommand(
    Guid ProjectId,
    string Name,
    string Transport,
    string? EndpointUrl = null,
    string? Command = null,
    string? Args = null,
    string? AuthType = null,
    string? AuthToken = null,
    string? AuthHeader = null,
    string? OAuthClientId = null,
    string? OAuthScopes = null) : ICommand<McpConnectionView>;
