using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Agents.Services;
using PlaceContext.Application.Ports;
using PlaceContext.AgentChat.Infrastructure.Slack;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Slack Events API ingress. Tenant from subdomain; project from <c>PlaceContext:Slack:ProjectId</c>.
/// Verifies the Slack signing secret, acks within the 3s window, then runs the agent and posts back.
/// Disabled (404) when Slack is not configured.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting("public-ingestion")]
public sealed class SlackController : ControllerBase
{
    private readonly SlackOptions _opts;
    private readonly ISlackThreadSessionStore _threads;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SlackController> _log;
    private readonly IClock _clock;

    public SlackController(
        IOptions<SlackOptions> opts,
        ISlackThreadSessionStore threads,
        IServiceScopeFactory scopes,
        ILogger<SlackController> log,
        IClock clock)
    {
        _opts = opts.Value;
        _threads = threads;
        _scopes = scopes;
        _log = log;
        _clock = clock;
    }

    [HttpPost("/slack/events")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Events()
    {
        if (!_opts.IsConfigured)
            return NotFound();

        // Need the raw body for signature verification.
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
        var bodyBytes = ms.ToArray();

        var timestamp = Request.Headers["X-Slack-Request-Timestamp"].ToString();
        var signature = Request.Headers["X-Slack-Signature"].ToString();
        if (!SlackSignature.IsValid(_opts.SigningSecret, timestamp, signature, bodyBytes, _clock.UtcNow))
            return Unauthorized();

        using var doc = JsonDocument.Parse(bodyBytes);
        var root = doc.RootElement;

        // URL verification challenge (Slack app setup).
        if (root.TryGetProperty("type", out var typeEl)
            && typeEl.GetString() == "url_verification"
            && root.TryGetProperty("challenge", out var challengeEl))
        {
            return Content(challengeEl.GetString() ?? "", "text/plain", Encoding.UTF8);
        }

        if (root.TryGetProperty("type", out var envelopeType)
            && envelopeType.GetString() == "event_callback")
        {
            var eventId = root.TryGetProperty("event_id", out var eid) ? eid.GetString() ?? "" : "";
            if (!await _threads.TryClaimEventAsync(eventId, HttpContext.RequestAborted))
                return Ok(); // already handled (Slack retry)

            if (!root.TryGetProperty("event", out var ev))
                return Ok();

            var teamId = root.TryGetProperty("team_id", out var tid) ? tid.GetString() ?? "" : "";
            if (!TryParseInbound(ev, out var inbound))
                return Ok();

            var projectId = Guid.Parse(_opts.ProjectId);
            var tenant = CurrentTenant.Current;
            if (tenant is null)
                return Ok();

            // Ack immediately; agent + Slack reply run detached (Events API 3s limit).
            _ = HandleDetachedAsync(tenant, projectId, teamId, inbound);
        }

        return Ok();
    }

    private async Task HandleDetachedAsync(TenantInfo tenant, Guid projectId, string teamId, InboundMessage msg)
    {
        try
        {
            CurrentTenant.Set(tenant);
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var bridge = scope.ServiceProvider.GetRequiredService<SlackAgentBridge>();
                await bridge.HandleUserMessageAsync(
                    projectId, teamId, msg.Channel, msg.Ts, msg.ThreadTs, msg.UserId, msg.Text,
                    CancellationToken.None);
            }
            finally { CurrentTenant.Clear(); }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Slack agent turn failed for channel {Channel}", msg.Channel);
        }
    }

    private static bool TryParseInbound(JsonElement ev, out InboundMessage msg)
    {
        msg = default!;
        var type = ev.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type is not ("message" or "app_mention"))
            return false;

        // Ignore bot/system noise.
        if (ev.TryGetProperty("bot_id", out _))
            return false;
        if (ev.TryGetProperty("subtype", out var sub) && !string.IsNullOrEmpty(sub.GetString()))
            return false;

        var channel = ev.TryGetProperty("channel", out var ch) ? ch.GetString() : null;
        var ts = ev.TryGetProperty("ts", out var tsEl) ? tsEl.GetString() : null;
        var text = ev.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(text))
            return false;

        // app_mention: strip <@BOTID> prefix so the model sees the real ask.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*<@[^>]+>\s*", "").Trim();
        if (text.Length == 0)
            return false;

        var userId = ev.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
        var threadTs = ev.TryGetProperty("thread_ts", out var th) ? th.GetString() : null;
        msg = new InboundMessage(channel!, ts!, threadTs, userId, text);
        return true;
    }

}
