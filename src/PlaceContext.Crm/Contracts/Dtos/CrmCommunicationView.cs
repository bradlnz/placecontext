namespace PlaceContext.Application.Features;

public sealed record CrmCommunicationView(
    Guid Id,
    Guid ClientId,
    string Channel,
    string? Subject,
    string Body,
    string? Recipient,
    string Status,
    string? Provider,
    string? Error,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);
