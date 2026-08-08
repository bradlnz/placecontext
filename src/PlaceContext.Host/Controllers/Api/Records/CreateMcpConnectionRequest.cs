namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record CreateMcpConnectionRequest(
    string Name,
    string Transport,
    string? EndpointUrl,
    string? Command,
    string? Args,
    string? AuthType,
    string? AuthToken,
    string? AuthHeader,
    string? OAuthScopes);
