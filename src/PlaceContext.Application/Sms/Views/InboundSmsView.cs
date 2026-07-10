namespace PlaceContext.Application.Features;

/// <summary>One inbound SMS, decrypted for an authorized reader.</summary>
public sealed record InboundSmsView(
    Guid Id, string From, string To, string Body, string Provider, Guid? ProjectId, DateTimeOffset ReceivedAt);
