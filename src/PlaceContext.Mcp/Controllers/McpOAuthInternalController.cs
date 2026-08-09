using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Mcp.Contracts.Api;

namespace PlaceContext.Mcp.Controllers;

[ApiController]
[Authorize(Policy = Permission.SettingsManage)]
[Route("api/mcp/internal/oauth/connections")]
[Produces("application/json")]
public sealed class McpOAuthInternalController(
    IMcpConnectionRepository connections,
    IMcpUnitOfWork unitOfWork,
    IDataEncryptor encryptor,
    IClock clock) : ControllerBase
{
    private const string EncryptPurpose = "mcp.oauth.tokens";

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<McpOAuthConnectionContext>> Get(Guid id, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        if (connection is null)
            return NotFound();

        return Ok(new McpOAuthConnectionContext(
            connection.Id,
            connection.Name,
            connection.EndpointUrl,
            connection.AuthType,
            connection.OAuthClientId,
            connection.OAuthScopes,
            Unprotect(connection.OAuthAccessToken),
            Unprotect(connection.OAuthRefreshToken),
            connection.OAuthTokenExpiresAt));
    }

    [HttpPut("{id:guid}/tokens")]
    public async Task<IActionResult> StoreTokens(
        Guid id,
        StoreMcpOAuthTokensRequest request,
        CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        if (connection is null)
            return NotFound();

        var accessToken = encryptor.Protect(request.AccessToken, EncryptPurpose);
        var refreshToken = string.IsNullOrEmpty(request.RefreshToken)
            ? connection.OAuthRefreshToken
            : encryptor.Protect(request.RefreshToken, EncryptPurpose);
        connection.StoreOAuthTokens(accessToken, refreshToken, request.ExpiresAt, clock.UtcNow);
        if (!string.IsNullOrWhiteSpace(request.ClientId))
            connection.SetOAuthCredentials(request.ClientId, connection.OAuthScopes, clock.UtcNow);
        connection.RecordConnection(request.Status, clock.UtcNow);
        await connections.UpdateAsync(connection, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        UpdateMcpOAuthStatusRequest request,
        CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(id, ct);
        if (connection is null)
            return NotFound();

        connection.RecordConnection(request.Status, clock.UtcNow);
        await connections.UpdateAsync(connection, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    private string? Unprotect(string? value)
        => string.IsNullOrEmpty(value) ? null : encryptor.Unprotect(value, EncryptPurpose);
}
