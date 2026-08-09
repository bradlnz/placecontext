namespace PlaceContext.Communications.Contracts;

public sealed record CommunicationEmailAttachment(
    string Name,
    string ContentType,
    string ContentBase64);
