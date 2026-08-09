namespace PlaceContext.Communications.Contracts;

public sealed record SendCommunicationEmailRequest(
    string Recipient,
    string RecipientName,
    string Subject,
    string Body,
    IReadOnlyList<CommunicationEmailAttachment>? Attachments = null,
    bool Authentication = false);
