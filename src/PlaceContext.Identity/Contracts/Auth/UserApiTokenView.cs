namespace PlaceContext.Application.Ports;

/// <summary>Metadata for a stored token — never includes the raw secret.</summary>
public sealed record UserApiTokenView(
    Guid Id,
    string Name,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt);
