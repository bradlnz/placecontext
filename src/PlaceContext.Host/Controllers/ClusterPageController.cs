using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Policy = Permission.SettingsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ClusterPageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClusterPageResponse>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var cluster = await placeContextService.GetClusterInfoAsync(cancellationToken);
            return Ok(new ClusterPageResponse(
                cluster.IsRealCluster,
                cluster.DesignatedMasterName,
                cluster.Nodes.Select(MapNode).ToList(),
                DateTimeOffset.Now.ToWorkspaceTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        }
        catch (Exception exception)
        {
            return StatusCode(502, new { error = $"Failed to load cluster info: {exception.Message}" });
        }
    }

    [HttpPost("workers/join-command")]
    public async Task<ActionResult<ClusterJoinCommandResponse>> CreateJoinCommand(
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await placeContextService.CreateAgentJoinTokenAsync(cancellationToken);
            var portal = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
            return Ok(new ClusterJoinCommandResponse(
                $"curl -fsSL {portal}/join.sh | bash -s -- --portal {portal} --token {token}"));
        }
        catch (Exception exception)
        {
            return StatusCode(502, new { error = $"Failed to create join token: {exception.Message}" });
        }
    }

    private static ClusterNodeResponse MapNode(ClusterNode node) =>
        new(
            node.Name,
            node.Roles,
            node.Ready,
            node.KubeletVersion,
            node.PreferredIp ?? "—",
            node.CpuCapacity ?? "—",
            node.MemoryCapacity ?? "—",
            node.IsSelf,
            node.IsControlPlane,
            node.IsDesignatedMaster,
            $"{(string.IsNullOrWhiteSpace(node.OperatingSystem) ? "Unknown OS" : node.OperatingSystem)} · {node.Architecture}",
            RelativeAge(node.CreatedAt));

    private static string RelativeAge(DateTimeOffset? createdAt)
    {
        if (createdAt is null)
            return "Local";
        var age = DateTimeOffset.UtcNow - createdAt.Value;
        if (age.TotalMinutes < 2)
            return "Just now";
        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
