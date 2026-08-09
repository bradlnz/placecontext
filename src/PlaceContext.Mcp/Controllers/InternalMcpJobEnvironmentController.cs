using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Route("api/mcp/internal/job-environment")]
public sealed class InternalMcpJobEnvironmentController(
    IMcpConnectionRepository connections,
    IDataEncryptor encryptor) : ControllerBase
{
    private const string EncryptPurpose = "mcp.oauth.tokens";

    [HttpPost]
    public async Task<IActionResult> Resolve(JobEnvironmentRequest request, CancellationToken ct)
    {
        var resolved = new List<object>();
        foreach (var id in request.ConnectionIds.Distinct())
        {
            var connection = await connections.GetByIdAsync(id, ct);
            if (connection is null || !connection.Enabled) continue;
            var token = connection.AuthType == "oauth"
                ? Unprotect(connection.OAuthAccessToken)
                : connection.AuthToken ?? string.Empty;
            resolved.Add(new { connection.Name, Url = connection.EndpointUrl ?? string.Empty, Token = token });
        }
        return Ok(resolved.Count == 0
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>
            {
                ["MCP_CONNECTIONS_JSON"] = JsonSerializer.Serialize(resolved),
            });
    }

    private string Unprotect(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : encryptor.Unprotect(value, EncryptPurpose) ?? string.Empty;

    public sealed record JobEnvironmentRequest(IReadOnlyList<Guid> ConnectionIds);
}
