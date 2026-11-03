namespace PlaceContext.Application.Jobs;

/// <summary>The caller owns and disposes Content.</summary>
public sealed record ArtifactContent(Stream Content, string ContentType, string FileName);
