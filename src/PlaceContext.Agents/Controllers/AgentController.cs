using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Agents.Cluster;
using PlaceContext.Agents.Contracts.Api;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Controllers;

[ApiController]
[Route("api/v1/agent")]
[Produces("application/json")]
public sealed class AgentController(
    IDispatcher dispatcher,
    IAgentTokenManager tokens,
    ICurrentTenant tenant,
    IAgentSecretProvider secrets,
    ITailscaleKeyMinter tailscaleKeyMinter,
    IClusterAdminPort clusterAdmin) : ControllerBase
{
    [HttpPost("tokens")]
    [Authorize(Policy = Permission.SettingsManage)]
    public async Task<IActionResult> CreateToken(CancellationToken cancellationToken)
    {
        var token = await dispatcher.Send(
            new CreateAgentJoinTokenCommand(),
            cancellationToken);
        var host = Request.Host.Value;
        return Ok(new
        {
            token,
            command = $"curl -fsSL https://{host}/join.sh | bash -s -- --portal https://{host} --token {token}",
        });
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> Exchange(
        [FromBody] AgentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "token is required" });

        var agentToken = await tokens.ConsumeTokenAsync(request.Token);
        if (agentToken is null)
            return Unauthorized(new { error = "Invalid or expired token" });
        if (agentToken.TenantId != tenant.TenantId)
            return Unauthorized(new { error = "Token tenant mismatch" });

        var values = await secrets.GetSecretsAsync(
            SystemProjects.Cluster,
            [
                LaunchClusterAgentHandler.ClientIdSecretName,
                LaunchClusterAgentHandler.ClientSecretSecretName,
                LaunchClusterAgentHandler.TagSecretName,
            ],
            cancellationToken);
        if (!values.TryGetValue(LaunchClusterAgentHandler.ClientIdSecretName, out var clientId)
            || !values.TryGetValue(LaunchClusterAgentHandler.ClientSecretSecretName, out var clientSecret))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Tailscale OAuth not configured. Add TS_CLIENT_ID and TS_CLIENT_SECRET to the vault (cluster system project).",
            });
        }

        var tags = values.GetValueOrDefault(
            LaunchClusterAgentHandler.TagSecretName,
            LaunchClusterAgentHandler.DefaultTag);
        if (string.IsNullOrWhiteSpace(tags))
            tags = LaunchClusterAgentHandler.DefaultTag;

        var tailscaleKey = await tailscaleKeyMinter.MintEphemeralAgentKeyAsync(
            clientId,
            clientSecret,
            tags,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(tailscaleKey))
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Failed to mint Tailscale auth key." });

        var join = await clusterAdmin.GetJoinMaterialAsync(tailscaleKey, cancellationToken);
        if (join is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Cluster join material not available (deploy the master first).",
            });
        }

        return Ok(new AgentExchangeResult(join.JoinCode, join.ServerUrl, join.Instructions));
    }
}
