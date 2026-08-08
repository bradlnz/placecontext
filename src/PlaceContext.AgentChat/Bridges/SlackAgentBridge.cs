using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Agents.Services;

/// <summary>
/// Handles one inbound Slack user message: resolve/create the thread session, run an agent turn
/// with tools, and post the final reply back into the Slack thread. Best-effort — never throws to
/// the caller after work starts (Slack already got the 200 ack).
/// </summary>
public sealed class SlackAgentBridge
{
    private readonly AgentSessionRunner _runner;
    private readonly ISlackThreadSessionStore _threads;
    private readonly ISlackClient _slack;
    private readonly IClock _clock;

    public SlackAgentBridge(
        AgentSessionRunner runner,
        ISlackThreadSessionStore threads,
        ISlackClient slack,
        IClock clock)
    {
        _runner = runner;
        _threads = threads;
        _slack = slack;
        _clock = clock;
    }

    public async Task HandleUserMessageAsync(
        Guid projectId,
        string teamId,
        string channelId,
        string messageTs,
        string? threadTs,
        string userId,
        string text,
        CancellationToken ct = default)
    {
        var rootTs = string.IsNullOrWhiteSpace(threadTs) ? messageTs : threadTs!;
        var sessionId = await _threads.GetOrCreateSessionIdAsync(teamId, channelId, rootTs, ct);
        var title = $"💬 Slack {channelId}";
        var userLine = string.IsNullOrWhiteSpace(userId) ? text : $"<@{userId}>: {text}";

        var reply = await _runner.RunChannelTurnAsync(projectId, sessionId, title, userLine, ct);
        if (string.IsNullOrWhiteSpace(reply))
            reply = "Done.";

        // Slack hard-caps message text; keep replies readable in-thread.
        if (reply.Length > 3500)
            reply = reply[..3500] + "…";

        if (_slack.IsEnabled)
            await _slack.PostMessageAsync(channelId, reply, rootTs, ct);
    }
}
