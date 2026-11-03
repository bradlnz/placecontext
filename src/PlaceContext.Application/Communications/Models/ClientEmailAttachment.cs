namespace PlaceContext.Application.Communications;

/// <summary>Provider-neutral email attachment. Content is RFC 4648 base64.</summary>
public sealed record ClientEmailAttachment(string Name, string ContentType, string ContentBase64);
