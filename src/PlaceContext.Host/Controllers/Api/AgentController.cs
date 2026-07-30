using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

[ApiController]
[Route("api/v1/agent")]
[Produces("application/json")]
public sealed class AgentController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    private readonly IAgentTokenManager _tokens;
    private readonly ICurrentTenant _tenant;
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;
    private readonly ITailscaleKeyMinter _minter;
    private readonly IClusterAdminPort _admin;

    public AgentController(
        IPlaceContextService svc,
        IAgentTokenManager tokens,
        ICurrentTenant tenant,
        IProjectSecretRepository secrets,
        ISecretProtector protector,
        ITailscaleKeyMinter minter,
        IClusterAdminPort admin)
        => (_svc, _tokens, _tenant, _secrets, _protector, _minter, _admin)
            = (svc, tokens, tenant, secrets, protector, minter, admin);

    [HttpPost("tokens")]
    [Authorize(AuthenticationSchemes = "Cookies")]
    public async Task<IActionResult> CreateToken(CancellationToken ct)
    {
        var token = await _svc.CreateAgentJoinTokenAsync(ct);
        var host = Request.Host.Value;
        return Ok(new
        {
            token,
            command = $"curl -fsSL https://{host}/join.sh | bash -s -- --portal https://{host} --token {token}",
        });
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange([FromBody] ExchangeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });

        var agentToken = await _tokens.ConsumeTokenAsync(request.Token);
        if (agentToken is null)
            return Unauthorized(new { error = "Invalid or expired token" });

        if (agentToken.TenantId != _tenant.TenantId)
            return Unauthorized(new { error = "Token tenant mismatch" });

        var ciphers = await _secrets.GetCiphersAsync(SystemProjects.Cluster, ct);
        if (!ciphers.TryGetValue(LaunchClusterAgentHandler.ClientIdSecretName, out var clientIdCipher)
            || !ciphers.TryGetValue(LaunchClusterAgentHandler.ClientSecretSecretName, out var clientSecretCipher))
        {
            return StatusCode(502, new
            {
                error = "Tailscale OAuth not configured. Add TS_CLIENT_ID and TS_CLIENT_SECRET to the vault (cluster system project).",
            });
        }

        var clientId = _protector.Unprotect(clientIdCipher);
        var clientSecret = _protector.Unprotect(clientSecretCipher);
        var tags = ciphers.TryGetValue(LaunchClusterAgentHandler.TagSecretName, out var tagCipher)
            ? _protector.Unprotect(tagCipher)
            : LaunchClusterAgentHandler.DefaultTag;
        if (string.IsNullOrWhiteSpace(tags)) tags = LaunchClusterAgentHandler.DefaultTag;

        var tsKey = await _minter.MintEphemeralAgentKeyAsync(clientId, clientSecret, tags, ct);
        if (string.IsNullOrWhiteSpace(tsKey))
            return StatusCode(502, new { error = "Failed to mint Tailscale auth key." });

        var join = await _admin.GetJoinMaterialAsync(tsKey, ct);
        if (join is null)
            return StatusCode(502, new { error = "Cluster join material not available (deploy the master first)." });

        return Ok(new ExchangeResult(join.JoinCode, join.ServerUrl, join.Instructions));
    }
}

public sealed record ExchangeRequest(string Token);

public sealed record ExchangeResult(string JoinCode, string ServerUrl, string Command);
