using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Agents.Contracts.Api;
using PlaceContext.Application.Cluster;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = Permission.SettingsManage)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ClusterPageController(
    IDispatcher dispatcher,
    ICurrentTenant tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClusterPageResponse>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var cluster = await dispatcher.Query(new GetClusterInfoQuery(), cancellationToken);
            return Ok(new ClusterPageResponse(
                cluster.IsRealCluster,
                cluster.DesignatedMasterName,
                cluster.Nodes.Select(MapNode).ToList(),
                ToWorkspaceTime(DateTimeOffset.UtcNow, tenant.TimeZoneId)
                    .ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        }
        catch (Exception exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = $"Failed to load cluster info: {exception.Message}",
            });
        }
    }

    [HttpPost("workers/join-command")]
    public async Task<ActionResult<ClusterJoinCommandResponse>> CreateJoinCommand(
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await dispatcher.Send(
                new CreateAgentJoinTokenCommand(),
                cancellationToken);
            var portal = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
            return Ok(new ClusterJoinCommandResponse(
                $"curl -fsSL {portal}/join.sh | bash -s -- --portal {portal} --token {token}"));
        }
        catch (Exception exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = $"Failed to create join token: {exception.Message}",
            });
        }
    }

    private static ClusterNodeResponse MapNode(ClusterNode node) => new(
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

    private static DateTimeOffset ToWorkspaceTime(DateTimeOffset value, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (TimeZoneNotFoundException)
        {
            return value;
        }
        catch (InvalidTimeZoneException)
        {
            return value;
        }
    }
}
