using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlaceContext.AgentChat.Slack;
using PlaceContext.Application.Agents.Services;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Controllers;

/// <summary>Slack Events API ingress owned by AgentChat.</summary>
[AllowAnonymous]
[EnableRateLimiting("public-ingestion")]
public sealed class SlackController : ControllerBase
{
    private readonly SlackOptions _options;
    private readonly ISlackThreadSessionStore _threads;
    private readonly IServiceScopeFactory _scopes;
    private readonly IRequestTenantResolver _tenantResolver;
    private readonly ILogger<SlackController> _logger;
    private readonly IClock _clock;

    public SlackController(
        IOptions<SlackOptions> options,
        ISlackThreadSessionStore threads,
        IServiceScopeFactory scopes,
        IRequestTenantResolver tenantResolver,
        ILogger<SlackController> logger,
        IClock clock)
    {
        _options = options.Value;
        _threads = threads;
        _scopes = scopes;
        _tenantResolver = tenantResolver;
        _logger = logger;
        _clock = clock;
    }

    [HttpPost("/slack/events")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Events(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            return NotFound();

        Request.EnableBuffering();
        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, cancellationToken);
        var bodyBytes = body.ToArray();

        var timestamp = Request.Headers["X-Slack-Request-Timestamp"].ToString();
        var signature = Request.Headers["X-Slack-Signature"].ToString();
        if (!SlackSignature.IsValid(
                _options.SigningSecret,
                timestamp,
                signature,
                bodyBytes,
                _clock.UtcNow))
        {
            return Unauthorized();
        }

        using var document = JsonDocument.Parse(bodyBytes);
        var root = document.RootElement;
        if (root.TryGetProperty("type", out var typeElement)
            && typeElement.GetString() == "url_verification"
            && root.TryGetProperty("challenge", out var challengeElement))
        {
            return Content(challengeElement.GetString() ?? string.Empty, "text/plain", Encoding.UTF8);
        }

        if (!root.TryGetProperty("type", out var envelopeType)
            || envelopeType.GetString() != "event_callback")
        {
            return Ok();
        }

        var eventId = root.TryGetProperty("event_id", out var eventIdElement)
            ? eventIdElement.GetString() ?? string.Empty
            : string.Empty;
        if (!await _threads.TryClaimEventAsync(eventId, cancellationToken))
            return Ok();
        if (!root.TryGetProperty("event", out var slackEvent)
            || !TryParseInbound(slackEvent, out var inbound))
        {
            return Ok();
        }

        var forwardedHost = Request.Headers["X-Forwarded-Host"].ToString().Split(',')[0].Trim();
        var requestHost = string.IsNullOrWhiteSpace(forwardedHost)
            ? Request.Host.Value ?? string.Empty
            : forwardedHost;
        var tenant = await _tenantResolver.ResolveAsync(requestHost, cancellationToken);
        if (tenant is null)
        {
            _logger.LogWarning("Slack event could not resolve tenant for host {Host}", requestHost);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var teamId = root.TryGetProperty("team_id", out var teamIdElement)
            ? teamIdElement.GetString() ?? string.Empty
            : string.Empty;
        var projectId = Guid.Parse(_options.ProjectId);
        _ = HandleDetachedAsync(tenant, projectId, teamId, inbound);
        return Ok();
    }

    private async Task HandleDetachedAsync(
        TenantContext tenant,
        Guid projectId,
        string teamId,
        InboundMessage message)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var tenantAccessor = scope.ServiceProvider.GetRequiredService<ICurrentTenantAccessor>();
            tenantAccessor.Set(tenant);
            try
            {
                var bridge = scope.ServiceProvider.GetRequiredService<SlackAgentBridge>();
                await bridge.HandleUserMessageAsync(
                    projectId,
                    teamId,
                    message.Channel,
                    message.Ts,
                    message.ThreadTs,
                    message.UserId,
                    message.Text,
                    CancellationToken.None);
            }
            finally
            {
                tenantAccessor.Clear();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Slack agent turn failed for channel {Channel}",
                message.Channel);
        }
    }

    private static bool TryParseInbound(JsonElement slackEvent, out InboundMessage message)
    {
        message = default!;
        var type = slackEvent.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
        if (type is not ("message" or "app_mention"))
            return false;
        if (slackEvent.TryGetProperty("bot_id", out _))
            return false;
        if (slackEvent.TryGetProperty("subtype", out var subtype)
            && !string.IsNullOrEmpty(subtype.GetString()))
        {
            return false;
        }

        var channel = slackEvent.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString()
            : null;
        var timestamp = slackEvent.TryGetProperty("ts", out var timestampElement)
            ? timestampElement.GetString()
            : null;
        var text = slackEvent.TryGetProperty("text", out var textElement)
            ? textElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"^\s*<@[^>]+>\s*",
            string.Empty).Trim();
        if (text.Length == 0)
            return false;

        var userId = slackEvent.TryGetProperty("user", out var userElement)
            ? userElement.GetString() ?? string.Empty
            : string.Empty;
        var threadTimestamp = slackEvent.TryGetProperty("thread_ts", out var threadElement)
            ? threadElement.GetString()
            : null;
        message = new InboundMessage(channel, timestamp, threadTimestamp, userId, text);
        return true;
    }
}
