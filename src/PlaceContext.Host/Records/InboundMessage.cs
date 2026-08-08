namespace PlaceContext.Host.Controllers;

internal sealed record InboundMessage(
    string Channel,
    string Ts,
    string? ThreadTs,
    string UserId,
    string Text);
