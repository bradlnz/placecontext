namespace PlaceContext.Application.Artifacts;

/// <summary>The one-time plaintext result returned when an artifact share link is created or rotated.</summary>
public sealed record ArtifactShareCreated(
    string Token,
    string TokenPrefix,
    DateTimeOffset ExpiresAt);
