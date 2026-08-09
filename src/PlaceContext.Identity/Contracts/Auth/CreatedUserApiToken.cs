namespace PlaceContext.Application.Ports;

/// <summary>Result of minting a token — <see cref="RawToken"/> is shown once then discarded.</summary>
public sealed record CreatedUserApiToken(
    Guid Id,
    string Name,
    string RawToken,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
