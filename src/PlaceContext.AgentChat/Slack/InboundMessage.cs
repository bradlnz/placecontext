namespace PlaceContext.AgentChat.Slack;

internal sealed record InboundMessage(
    string Channel,
    string Ts,
    string? ThreadTs,
    string UserId,
    string Text);
