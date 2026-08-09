namespace PlaceContext.Application.Ports;

/// <summary>Provider-neutral email attachment. Content is RFC 4648 base64.</summary>
public sealed record ClientEmailAttachment(string Name, string ContentType, string ContentBase64);
