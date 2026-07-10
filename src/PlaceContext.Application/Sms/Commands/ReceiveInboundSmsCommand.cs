using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Accept one inbound SMS from the gateway webhook. The sender and body are encrypted before
/// anything touches the store; the emitted <c>sms.received</c> event (which can fire job triggers)
/// carries only routing metadata and a masked sender — never the message text.
/// </summary>
public sealed record ReceiveInboundSmsCommand(
    string From, string To, string Body, string Provider, string? ExternalId, Guid? ProjectId)
    : ICommand<InboundSmsView>;
