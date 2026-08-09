namespace PlaceContext.Application.Ports;

/// <summary>Storage coordinates disclosed after a public artifact token is validated.</summary>
public sealed record SharedArtifact(
    string Title,
    string Bucket,
    string ObjectKey,
    string ContentType);
