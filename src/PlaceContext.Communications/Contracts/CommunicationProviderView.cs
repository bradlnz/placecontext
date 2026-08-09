namespace PlaceContext.Communications.Contracts;

public sealed record CommunicationProviderView(
    Guid Id,
    string Channel,
    string Kind,
    string Name,
    bool Enabled,
    bool IsDefault,
    bool UseForTwoFactor,
    string AuthType,
    string? AuthHeaderName,
    Guid? VaultProjectId,
    string? ApiKeySecretName,
    string SettingsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
