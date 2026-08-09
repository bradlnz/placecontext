using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp;

internal static class McpConnectionMapper
{
    internal static McpConnectionView ToView(McpConnection connection) => new(
        connection.Id,
        connection.ProjectId,
        connection.Name,
        connection.Transport,
        connection.EndpointUrl,
        connection.Command,
        connection.Args,
        connection.AuthType,
        connection.Enabled,
        connection.LastStatus,
        connection.LastConnectedAt,
        connection.CreatedAt,
        connection.OAuthAccessToken,
        connection.OAuthTokenExpiresAt,
        connection.OAuthClientId,
        connection.OAuthScopes);
}
