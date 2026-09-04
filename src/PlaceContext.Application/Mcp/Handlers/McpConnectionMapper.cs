using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class McpConnectionMapper
{
    internal static McpConnectionView ToView(McpConnection c) => new(
        c.Id, c.ProjectId, c.Name, c.Transport, c.EndpointUrl, c.Command, c.Args,
        c.AuthType, c.Enabled, c.LastStatus, c.LastConnectedAt, c.CreatedAt,
        c.OAuthAccessToken, c.OAuthTokenExpiresAt, c.OAuthClientId, c.OAuthScopes);
}
