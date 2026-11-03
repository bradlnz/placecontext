namespace PlaceContext.Application.Artifacts;

/// <summary>Storage coordinates disclosed internally after a public bearer token is validated.</summary>
public sealed record SharedArtifact(
    string Title,
    string Bucket,
    string ObjectKey,
    string ContentType);
