namespace PlaceContext.Host.Api;

public sealed record InboundEmailRequest(string MessageID, string From, string To, string Subject, string TextBody, string? Date);
